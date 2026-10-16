namespace JobPilot.Desktop.Core;

/// <summary>The last lines the server wrote, shown to the owner when it fails to start. Kept in memory only.</summary>
public sealed class ServerOutput(int capacity = 40)
{
    private readonly Queue<string> _lines = new();
    private readonly Lock _gate = new();

    public void Add(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_gate)
        {
            _lines.Enqueue(line);
            while (_lines.Count > capacity)
            {
                _lines.Dequeue();
            }
        }
    }

    public string Text
    {
        get
        {
            lock (_gate)
            {
                return string.Join(Environment.NewLine, _lines);
            }
        }
    }
}
