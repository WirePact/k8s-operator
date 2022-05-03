using System.Security.Cryptography;
using System.Text;

namespace Operator;

// Until envoy supports simple building of their proto files, we have to
// statically generate the config (and hope that it works).
internal static class EnvoyConfig
{
    public static (string Config, string ConfigHash) Bootstrap(
        int ingressPort,
        int egressPort,
        int appPort,
        int translatorIngressPort,
        int translatorEgressPort)
    {
        var config = $@"
admin:
  access_log:
    - name: envoy.access_loggers.stdout
      typed_config:
        '@type': type.googleapis.com/envoy.extensions.access_loggers.stream.v3.StdoutAccessLog

static_resources:
  listeners:
    - name: ingress_listener
      address:
        socket_address:
          protocol: TCP
          address: 0.0.0.0
          port_value: {ingressPort}
      filter_chains:
        - filters:
            - name: envoy.filters.network.http_connection_manager
              typed_config:
                '@type': type.googleapis.com/envoy.extensions.filters.network.http_connection_manager.v3.HttpConnectionManager
                stat_prefix: ingress_http
                access_log:
                  - name: envoy.access_loggers.file
                    typed_config:
                      '@type': type.googleapis.com/envoy.extensions.access_loggers.file.v3.FileAccessLog
                      path: /dev/stdout
                route_config:
                  name: local_route
                  virtual_hosts:
                    - name: local_service
                      domains: [ '*' ]
                      routes:
                        - match:
                            prefix: '/'
                          route:
                            cluster: target_service
                http_filters:
                  - name: envoy.filters.http.ext_authz
                    typed_config:
                      '@type': type.googleapis.com/envoy.extensions.filters.http.ext_authz.v3.ExtAuthz
                      transport_api_version: v3
                      grpc_service:
                        envoy_grpc:
                          cluster_name: auth_translator_ingress
                        timeout: 1s
                      include_peer_certificate: true
                  - name: envoy.filters.http.router
                    typed_config:
                      '@type': type.googleapis.com/envoy.extensions.filters.http.router.v3.Router

    - name: egress_listener
      address:
        socket_address:
          protocol: TCP
          address: 0.0.0.0
          port_value: {egressPort}
      filter_chains:
        - filters:
            - name: envoy.filters.network.http_connection_manager
              typed_config:
                '@type': type.googleapis.com/envoy.extensions.filters.network.http_connection_manager.v3.HttpConnectionManager
                stat_prefix: egress_http
                access_log:
                  - name: envoy.access_loggers.file
                    typed_config:
                      '@type': type.googleapis.com/envoy.extensions.access_loggers.file.v3.FileAccessLog
                      path: /dev/stdout
                route_config:
                  name: local_route
                  virtual_hosts:
                    - name: local_service
                      domains: [ '*' ]
                      routes:
                        - match:
                            prefix: '/'
                          route:
                            cluster: dynamic_forward_proxy_cluster
                http_filters:
                  - name: envoy.filters.http.ext_authz
                    typed_config:
                      '@type': type.googleapis.com/envoy.extensions.filters.http.ext_authz.v3.ExtAuthz
                      transport_api_version: v3
                      grpc_service:
                        envoy_grpc:
                          cluster_name: auth_translator_egress
                        timeout: 1s
                      include_peer_certificate: true
                  - name: envoy.filters.http.dynamic_forward_proxy
                    typed_config:
                      '@type': type.googleapis.com/envoy.extensions.filters.http.dynamic_forward_proxy.v3.FilterConfig
                      dns_cache_config:
                        name: dynamic_forward_proxy_cache_config
                        dns_lookup_family: V4_ONLY
                        typed_dns_resolver_config:
                          name: envoy.network.dns_resolver.cares
                          typed_config:
                            '@type': type.googleapis.com/envoy.extensions.network.dns_resolver.cares.v3.CaresDnsResolverConfig
                            resolvers:
                              - socket_address:
                                  address: '8.8.8.8'
                                  port_value: 53
                            use_resolvers_as_fallback: true
                            dns_resolver_options:
                              use_tcp_for_dns_lookups: true
                              no_default_search_domain: true
                  - name: envoy.filters.http.router
                    typed_config:
                      '@type': type.googleapis.com/envoy.extensions.filters.http.router.v3.Router

  clusters:
    - name: target_service
      connect_timeout: 30s
      type: LOGICAL_DNS
      load_assignment:
        cluster_name: target_service
        endpoints:
          - lb_endpoints:
              - endpoint:
                  address:
                    socket_address:
                      address: 127.0.0.1
                      port_value: {appPort}

    - name: auth_translator_ingress
      connect_timeout: 0.25s
      type: STRICT_DNS
      typed_extension_protocol_options:
        envoy.extensions.upstreams.http.v3.HttpProtocolOptions:
          '@type': type.googleapis.com/envoy.extensions.upstreams.http.v3.HttpProtocolOptions
          explicit_http_config:
            http2_protocol_options: {{}}
      load_assignment:
        cluster_name: auth_translator_ingress
        endpoints:
          - lb_endpoints:
              - endpoint:
                  address:
                    socket_address:
                      address: 127.0.0.1
                      port_value: {translatorIngressPort}

    - name: auth_translator_egress
      connect_timeout: 0.25s
      type: STRICT_DNS
      typed_extension_protocol_options:
        envoy.extensions.upstreams.http.v3.HttpProtocolOptions:
          '@type': type.googleapis.com/envoy.extensions.upstreams.http.v3.HttpProtocolOptions
          explicit_http_config:
            http2_protocol_options: {{}}
      load_assignment:
        cluster_name: auth_translator_egress
        endpoints:
          - lb_endpoints:
              - endpoint:
                  address:
                    socket_address:
                      address: 127.0.0.1
                      port_value: {translatorEgressPort}

    - name: dynamic_forward_proxy_cluster
      lb_policy: CLUSTER_PROVIDED
      cluster_type:
        name: envoy.clusters.dynamic_forward_proxy
        typed_config:
          '@type': type.googleapis.com/envoy.extensions.clusters.dynamic_forward_proxy.v3.ClusterConfig
          dns_cache_config:
            name: dynamic_forward_proxy_cache_config
            dns_lookup_family: V4_ONLY
            typed_dns_resolver_config:
              name: envoy.network.dns_resolver.cares
              typed_config:
                '@type': type.googleapis.com/envoy.extensions.network.dns_resolver.cares.v3.CaresDnsResolverConfig
                resolvers:
                  - socket_address:
                      address: '8.8.8.8'
                      port_value: 53
                use_resolvers_as_fallback: true
                dns_resolver_options:
                  use_tcp_for_dns_lookups: true
                  no_default_search_domain: true
";
        using var hash = SHA256.Create();
        var configHash = hash.ComputeHash(Encoding.UTF8.GetBytes(config));
        var sb = new StringBuilder();
        foreach (var b in configHash)
        {
            sb.Append(b.ToString("x2"));
        }

        return (config, sb.ToString());
    }
}
