using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommandEditor.Core.Models;

namespace CommandEditor.Core.Services;

public class HistoryService
{
    private readonly AppPaths _paths;

    public HistoryService(AppPaths paths)
    {
        _paths = paths;
        Directory.CreateDirectory(_paths.HistoryDirectory);
    }

    public async Task<HistoryEntry> SaveBackupAsync(IEnumerable<CommandItem> commands, int maxBackups = 100, CancellationToken cancellationToken = default)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"commands_{timestamp}.json";
        var filePath = Path.Combine(_paths.HistoryDirectory, fileName);
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, commands, cancellationToken: cancellationToken);
        var info = new FileInfo(filePath);
        await CleanupAsync(maxBackups);
        return new HistoryEntry
        {
            FilePath = filePath,
            CreatedAtUtc = info.CreationTimeUtc,
            SizeBytes = info.Length
        };
    }

    public Task<IReadOnlyList<HistoryEntry>> GetBackupsAsync()
    {
        var entries = Directory.GetFiles(_paths.HistoryDirectory, "commands_*.json")
            .Select(file =>
            {
                var info = new FileInfo(file);
                return new HistoryEntry
                {
                    FilePath = file,
                    CreatedAtUtc = info.CreationTimeUtc,
                    SizeBytes = info.Length
                };
            })
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();
        return Task.FromResult<IReadOnlyList<HistoryEntry>>(entries);
    }

    public async Task<IList<CommandItem>> RestoreAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        var commands = await JsonSerializer.DeserializeAsync<IList<CommandItem>>(stream, cancellationToken: cancellationToken);
        return commands ?? new List<CommandItem>();
    }

    private Task CleanupAsync(int maxBackups = 100)
    {
        var files = Directory.GetFiles(_paths.HistoryDirectory, "commands_*.json")
            .OrderByDescending(x => x)
            .ToList();
        foreach (var file in files.Skip(maxBackups))
        {
            File.Delete(file);
        }

        return Task.CompletedTask;
    }
}
