namespace ApiLifecycle;

public interface IApiLifecycleRegistry
{
    IReadOnlyCollection<ApiVersionPolicy> Policies { get; }
    bool TryGetPolicy(string version, out ApiVersionPolicy policy);
}
