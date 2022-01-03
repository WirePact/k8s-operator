using System.Collections.Generic;
using System.Linq;
using k8s.Models;

namespace WirePact.Operator;

internal static class EntityExtensions
{
    public static bool EnsureEnvVar(this V1Container container, string name, string value)
    {
        container.Env ??= new List<V1EnvVar>();
        var envVar = container.Env.FirstOrDefault(e => e.Name == name);

        if (envVar == null)
        {
            container.Env.Add(new V1EnvVar(name, value));
            return true;
        }

        if (envVar.Value == value)
        {
            return false;
        }

        envVar.Value = value;
        return true;
    }

    public static bool EnsurePort(this V1Container container, int port, string? name = null)
    {
        container.Ports ??= new List<V1ContainerPort>();
        var containerPort =
            container.Ports.FirstOrDefault(
                p => p.ContainerPort == port);

        if (containerPort == null)
        {
            container.Ports.Add(new V1ContainerPort(port, name: name));
            return true;
        }

        if (containerPort.Name == name)
        {
            return false;
        }

        containerPort.Name = name;
        return true;
    }
}
