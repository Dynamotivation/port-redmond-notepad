using System.Text;

namespace Redmond.Notepad.Core;

/// <summary>
/// Registers one running Notepad process. Lease files remain after a crash, but
/// their exclusive handles are released by the operating system and are pruned
/// by the next launcher.
/// </summary>
public sealed class NotepadProcessRegistration : IDisposable
{
    private readonly FileStream _lease;
    private readonly string _leasePath;
    private bool _disposed;

    private NotepadProcessRegistration(
        FileStream lease,
        string leasePath,
        bool hasOtherActiveProcesses)
    {
        _lease = lease;
        _leasePath = leasePath;
        HasOtherActiveProcesses = hasOtherActiveProcesses;
    }

    public bool HasOtherActiveProcesses { get; }

    public static NotepadProcessRegistration Register(string settingsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDirectory);
        var instancesDirectory = Path.Combine(
            Path.GetFullPath(settingsDirectory),
            "instances");
        Directory.CreateDirectory(instancesDirectory);
        var registryLockPath = Path.Combine(instancesDirectory, "registry.lock");
        using var registryLock = AcquireExclusiveFile(registryLockPath);

        var hasOtherActiveProcesses = false;
        foreach (var path in Directory.EnumerateFiles(instancesDirectory, "*.lease"))
        {
            try
            {
                using var staleLease = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None);
                staleLease.Dispose();
                File.Delete(path);
            }
            catch (IOException)
            {
                hasOtherActiveProcesses = true;
            }
            catch (UnauthorizedAccessException)
            {
                hasOtherActiveProcesses = true;
            }
        }

        var leasePath = Path.Combine(
            instancesDirectory,
            $"{Environment.ProcessId}-{Guid.NewGuid():N}.lease");
        var lease = new FileStream(
            leasePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.Read);
        var description = Encoding.UTF8.GetBytes(
            $"{Environment.ProcessId}\n{DateTimeOffset.UtcNow:O}\n");
        lease.Write(description);
        lease.Flush(flushToDisk: true);
        return new NotepadProcessRegistration(
            lease,
            leasePath,
            hasOtherActiveProcesses);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lease.Dispose();
        try
        {
            File.Delete(_leasePath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static FileStream AcquireExclusiveFile(string path)
    {
        IOException? lastException = null;
        for (var attempt = 0; attempt < 200; attempt++)
        {
            try
            {
                return new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException exception)
            {
                lastException = exception;
                Thread.Sleep(25);
            }
        }

        throw new IOException("Timed out waiting for the Notepad process-registry lock.", lastException);
    }
}
