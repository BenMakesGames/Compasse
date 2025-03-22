namespace Compasse;

public interface IPrompt
{
    static abstract string Name { get; }
    static abstract string Description { get; }
}

public interface IPrompt<TRequest, TResponse>: IPrompt
{
    TResponse Execute(TRequest request);
}
