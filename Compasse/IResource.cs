namespace Compasse;

public interface IResource
{
    static abstract string Name { get; }
    static abstract string Description { get; }
    static abstract string Uri { get; }
    static abstract string MimeType { get; }
}
