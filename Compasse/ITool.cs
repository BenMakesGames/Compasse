namespace Compasse;

public interface ITool
{
    static abstract string Name { get; }
    static abstract string Description { get; }
}

public interface ITool<TRequest>: ITool
{
    ToolResponse Execute(TRequest request);
}

public abstract class ToolResponse
{
    public required List<IToolResponseContent> Content { get; init; }
}

public interface IToolResponseContent
{
    string Type { get; }
}

public class ToolResponseText: IToolResponseContent
{
    public string Type => "text";
    public string Text { get; }

    public ToolResponseText(string text)
    {
        Text = text;
    }
}
