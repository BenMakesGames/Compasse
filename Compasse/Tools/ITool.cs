namespace Compasse.Tools;

public interface ITool
{
    static abstract string Method { get; }
    static abstract string Description { get; }
}

public interface ITool<TRequest, TResponse>: ITool
{
    TResponse Execute(TRequest request);
}
