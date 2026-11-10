using System.Collections.Concurrent;
using System.Text;
using MessagePack;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Agents;
using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Infrastructure.Storage;

/// <summary>
/// In-memory Merkle DAG store with disk persistence using FASTER DB format.
/// Provides content-addressable storage with optional type information for deserialization.
/// Thread-safe with async locking for concurrent access.
/// </summary>
public class FasterMerkleStore : IMerkleStore, IAsyncDisposable
{
    /// <summary>
    /// Internal storage format wrapping the serialized payload with type information.
    /// </summary>
    [MessagePackObject(AllowPrivate = true)]
    internal readonly record struct StoredBlob([property: Key(0)] string Type, [property: Key(1)] byte[] Payload);

    /// <summary>
    /// Whitelist of types allowed for deserialization from StoredBlob.
    /// Keys are the full Type.AssemblyQualifiedName format stored by the serializer.
    /// Values are the concrete Type instances.
    /// </summary>
    private static readonly Dictionary<string, Type> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [typeof(Cid).AssemblyQualifiedName!] = typeof(Cid),
        [typeof(MerkleNode).AssemblyQualifiedName!] = typeof(MerkleNode),
        [typeof(StateManifest).AssemblyQualifiedName!] = typeof(StateManifest),
        [typeof(LatticeCommit).AssemblyQualifiedName!] = typeof(LatticeCommit),
        [typeof(DialecticalNode).AssemblyQualifiedName!] = typeof(DialecticalNode),
        [typeof(ArgumentClaimNode).AssemblyQualifiedName!] = typeof(ArgumentClaimNode),
        [typeof(IngestedDocument).AssemblyQualifiedName!] = typeof(IngestedDocument),
        [typeof(AgentSignature).AssemblyQualifiedName!] = typeof(AgentSignature),
        [typeof(MediationContext).AssemblyQualifiedName!] = typeof(MediationContext),
        [typeof(Citation).AssemblyQualifiedName!] = typeof(Citation),
        [typeof(DebateTurnEvent).AssemblyQualifiedName!] = typeof(DebateTurnEvent),
        [typeof(DialogueLevelResult).AssemblyQualifiedName!] = typeof(DialogueLevelResult),
        [typeof(Session).AssemblyQualifiedName!] = typeof(Session),
        [typeof(LlmAuditNode).AssemblyQualifiedName!] = typeof(LlmAuditNode),
    };

    /// <summary>
    /// Resolves a stored type name to a Type using the whitelist.
    /// Returns null if the type is not in the whitelist.
    /// </summary>
    private static Type? ResolveType(string typeName)
    {
        if (AllowedTypes.TryGetValue(typeName, out var type))
            return type;

        // Fallback: match by trimming version/culture/publickey info.
        // Stored format: "Namespace.Type, Assembly, Version=x, Culture=x, PublicKeyToken=x"
        // Whitelist keys: "Namespace.Type, Assembly"
        var commaIndex = typeName.IndexOf(',');
        if (commaIndex > 0)
        {
            var shortName = typeName[..commaIndex].Trim();
            foreach (var kv in AllowedTypes)
            {
                var keyShort = kv.Key[..kv.Key.IndexOf(',')].Trim();
                if (string.Equals(keyShort, shortName, StringComparison.Ordinal))
                    return kv.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// In-memory key-value store for serialized data.
    /// Key format: "obj:{cid}" for objects, "head:{userId}" for commit heads.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte[]> _store = new();

    /// <summary>
    /// Async semaphore for thread-safe write operations.
    /// </summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Base directory for disk persistence.
    /// </summary>
    private readonly string _baseDir;
    private readonly int _defaultHistoryLimit;
    private readonly int _compactionFactor;

    /// <summary>
    /// Number of log lines appended since the last full rewrite. Used to decide
    /// when compaction should fire.
    /// </summary>
    private int _appendedLines;

    /// <summary>
    /// How many times a persist is retried when store.dat is transiently locked
    /// (e.g. an antivirus/indexer scan or a briefly coexisting second instance).
    /// Only exercised during compaction, since normal writes are append-only.
    /// </summary>
    private const int MaxPersistRetries = 4;

    /// <summary>
    /// Initializes the store with disk persistence from the specified directory.
    /// Creates log and checkpoint directories if they don't exist.
    /// </summary>
    /// <param name="baseDir">The base directory for storing persisted data.</param>
    /// <param name="defaultHistoryLimit">Default max entries returned by GetHistoryAsync when no limit is specified.</param>
    /// <param name="compactionFactor">Log lines written per live key before a full rewrite fires (default: 5x).</param>
    public FasterMerkleStore(string baseDir, int defaultHistoryLimit = 100, int compactionFactor = 5)
    {
        _baseDir = Path.GetFullPath(baseDir);
        _defaultHistoryLimit = defaultHistoryLimit;
        _compactionFactor = Math.Max(1, compactionFactor);
        var logDir = Path.Combine(_baseDir, "log");
        var checkDir = Path.Combine(_baseDir, "checkpoints");

        Directory.CreateDirectory(logDir);
        Directory.CreateDirectory(checkDir);

        CleanupOrphanedTempFiles();
        LoadFromDisk();
    }

    /// <summary>
    /// Deletes any store.*.tmp files left behind by a previously interrupted
    /// persist (crash/kill between the temp write and the atomic move).
    /// </summary>
    private void CleanupOrphanedTempFiles()
    {
        try
        {
            foreach (var tempFile in Directory.GetFiles(_baseDir, "store.*.tmp"))
            {
                try { File.Delete(tempFile); }
                catch { /* best-effort */ }
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    /// <summary>
    /// Loads persisted data from store.dat into memory.
    /// Format: "key|base64data" per line.
    /// Tolerates a partial trailing line left by a crash mid-append by skipping it.
    /// </summary>
    private void LoadFromDisk()
    {
        var dataFile = Path.Combine(_baseDir, "store.dat");
        if (!File.Exists(dataFile)) return;

        try
        {
            var lines = File.ReadAllLines(dataFile);
            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                // Last-write-wins replay: a later append for the same key
                // naturally overwrites the earlier in-memory value.
                try
                {
                    var parts = line.Split('|', 2);
                    if (parts.Length != 2) continue;
                    _store[parts[0]] = Convert.FromBase64String(parts[1]);
                }
                catch (FormatException)
                {
                    // Skip a partial/corrupt line (e.g. truncated mid-append) rather
                    // than failing the whole load. Data before it is intact.
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DIAG-STORE] Failed to load persisted store: {ex.Message}");
        }
    }

    /// <summary>
    /// Appends a single "key|base64data" line to store.dat. O(1) per write,
    /// unlike the old full-rewrite-per-write pattern.
    /// </summary>
    private async Task AppendToDiskAsync(string key, byte[] data)
    {
        var dataFile = Path.Combine(_baseDir, "store.dat");
        var line = $"{key}|{Convert.ToBase64String(data)}{Environment.NewLine}";
        await File.AppendAllTextAsync(dataFile, line);

        _appendedLines++;
        if (_appendedLines >= _store.Count * _compactionFactor)
        {
            await CompactDiskAsync();
        }
    }

    /// <summary>
    /// Performs a one-time full rewrite of store.dat via a temp file + atomic
    /// move, then resets the append counter. Only runs during compaction, so
    /// the retry-on-lock logic is exercised far less often than before.
    /// </summary>
    private async Task CompactDiskAsync()
    {
        var dataFile = Path.Combine(_baseDir, "store.dat");
        var tempFile = Path.Combine(_baseDir, $"store.{Guid.NewGuid():N}.tmp");

        try
        {
            var contents = _store.Select(kvp => $"{kvp.Key}|{Convert.ToBase64String(kvp.Value)}");
            await File.WriteAllLinesAsync(tempFile, contents);

            Exception? lastError = null;
            for (var attempt = 1; attempt <= MaxPersistRetries; attempt++)
            {
                try
                {
                    File.Move(tempFile, dataFile, true);
                    _appendedLines = 0;
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    lastError = ex;
                    await Task.Delay(50 * attempt);
                }
            }

            Console.WriteLine(
                $"[DIAG-STORE] Failed to compact store after {MaxPersistRetries} attempts: {lastError?.Message}. " +
                $"dataFile={dataFile}. If another SeldonLattice.API instance is running against the same " +
                "DataDir, stop it - two processes cannot safely share store.dat.");
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
    }

    /// <summary>
    /// Creates a storage key for a CID.
    /// </summary>
    private static string Key(Cid cid) => $"obj:{cid}";

    /// <summary>
    /// Creates a storage key for a companion signature blob.
    /// </summary>
    private static string SignatureKey(Cid signedCid) => $"sig:{signedCid}";

    /// <summary>
    /// Stores a Merkle node with type information for later deserialization.
    /// </summary>
    /// <param name="node">The node to store.</param>
    /// <returns>The CID of the stored node.</returns>
    public async Task<Cid> PutBlobAsync(IMerkleNode node)
    {
        var cid = node.GetCid();
        var key = Key(cid);
        var payload = node.Serialize();
        var blob = new StoredBlob(node.GetType().AssemblyQualifiedName ?? node.GetType().FullName ?? string.Empty, payload);
        var data = MessagePackSerializer.Serialize(blob);

        await _lock.WaitAsync();
        try
        {
            _store[key] = data;
            await AppendToDiskAsync(key, data);
        }
        finally
        {
            _lock.Release();
        }

        if (node is LlmAuditNode auditNode && !string.IsNullOrWhiteSpace(auditNode.SessionId))
        {
            await AppendAuditIndexAsync(auditNode.SessionId, cid.ToString());
        }

        return cid;
    }

    /// <summary>
    /// Appends an audit node CID to the secondary session index.
    /// </summary>
    private async Task AppendAuditIndexAsync(string sessionId, string cidStr)
    {
        var indexKey = $"aidx:{sessionId}";
        await _lock.WaitAsync();
        try
        {
            string[] existing = _store.TryGetValue(indexKey, out var raw)
                ? MessagePackSerializer.Deserialize<string[]>(raw)
                : [];
            var updated = existing.Append(cidStr).ToArray();
            var data = MessagePackSerializer.Serialize(updated);
            _store[indexKey] = data;
            await AppendToDiskAsync(indexKey, data);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DIAG-STORE] Failed to append audit session index for {sessionId}: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Returns all <see cref="LlmAuditNode"/> records for the given session in Timestamp ascending order.
    /// </summary>
    public async IAsyncEnumerable<LlmAuditNode> GetAuditNodesAsync(string sessionId)
    {
        var indexKey = $"aidx:{sessionId}";
        string[]? cids = null;
        var indexUsed = false;

        if (_store.TryGetValue(indexKey, out var raw))
        {
            try
            {
                cids = MessagePackSerializer.Deserialize<string[]>(raw);
                indexUsed = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DIAG-STORE] Audit session index corrupted for {sessionId}: {ex.Message}; falling back to scan");
            }
        }

        if (indexUsed && cids is not null)
        {
            var batch = new List<LlmAuditNode>(50);
            foreach (var cidStr in cids)
            {
                LlmAuditNode node;
                try
                {
                    node = await GetBlobAsync<LlmAuditNode>(Cid.FromString(cidStr));
                }
                catch (KeyNotFoundException)
                {
                    continue;
                }

                batch.Add(node);
                if (batch.Count == 50)
                {
                    foreach (var n in batch.OrderBy(x => x.Timestamp))
                    {
                        yield return n;
                    }
                    batch.Clear();
                }
            }

            foreach (var n in batch.OrderBy(x => x.Timestamp))
            {
                yield return n;
            }
        }
        else
        {
            if (!indexUsed)
            {
                Console.WriteLine($"[DIAG-STORE] No audit session index for {sessionId}; scanning all stored LlmAuditNode records");
            }

            var all = await GetAllNodesAsync<LlmAuditNode>();
            foreach (var n in all.Where(x => x.SessionId == sessionId).OrderBy(x => x.Timestamp))
            {
                yield return n;
            }
        }
    }

    /// <summary>
    /// Retrieves a typed node from storage by its CID.
    /// Handles type mismatch by attempting fallback deserialization.
    /// </summary>
    /// <param name="cid">The content identifier of the node.</param>
    /// <returns>The deserialized node of the requested type.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the CID is not found.</exception>
    public async Task<T> GetBlobAsync<T>(Cid cid) where T : IMerkleNode
    {
        var key = Key(cid);
        if (!_store.TryGetValue(key, out var data))
        {
            throw new KeyNotFoundException($"CID {cid} not found");
        }

        try
        {
            var blob = MessagePackSerializer.Deserialize<StoredBlob>(data);
            if (ResolveType(blob.Type) is { } actualType && typeof(T).IsAssignableFrom(actualType))
            {
                return MessagePackSerializer.Deserialize<T>(blob.Payload);
            }
        }
        catch
        {
            // Fallback to legacy raw payload format (pre-type-wrapper).
        }

        return MessagePackSerializer.Deserialize<T>(data);
    }

    /// <summary>
    /// Creates a user head key for tracking the current commit.
    /// </summary>
    private static string HeadKey(string userId) => $"head:{userId}";

    /// <summary>
    /// Gets the current head commit CID for a user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <returns>The head CID if one exists, or null.</returns>
    public async Task<Cid?> GetHeadAsync(string userId)
    {
        var key = HeadKey(userId);
        if (_store.TryGetValue(key, out var data))
        {
            return Cid.FromString(Encoding.UTF8.GetString(data));
        }
        return await Task.FromResult<Cid?>(null);
    }

    /// <summary>
    /// Updates the head commit CID for a user after a new commit is created.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="commitCid">The new head commit CID.</param>
    public async Task UpdateHeadAsync(string userId, Cid commitCid)
    {
        var key = HeadKey(userId);
        await _lock.WaitAsync();
        try
        {
            var data = Encoding.UTF8.GetBytes(commitCid.ToString());
            _store[key] = data;
            await AppendToDiskAsync(key, data);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Creates a new commit with the given manifest and updates the user's head.
    /// </summary>
    /// <param name="userId">The user creating the commit.</param>
    /// <param name="message">Commit message describing the changes.</param>
    /// <param name="manifest">The state manifest containing all node references.</param>
    /// <returns>The CID of the created commit.</returns>
    public async Task<Cid> CreateCommitAsync(string userId, string message, StateManifest manifest)
    {
        var manifestCid = await PutBlobAsync(manifest);
        var parentCid = await GetHeadAsync(userId);
        var commit = new LatticeCommit
        {
            RootManifestCid = manifestCid.ToString(),
            ParentCommitCid = parentCid?.ToString(),
            Timestamp = DateTime.UtcNow,
            Message = message,
            UserId = userId
        };
        var commitCid = await PutBlobAsync(commit);
        await UpdateHeadAsync(userId, commitCid);
        return commitCid;
    }

    /// <summary>
    /// Gets the commit history for a user by traversing the commit chain backwards.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="limit">Maximum number of commits to return.</param>
    /// <returns>Array of commits from newest to oldest.</returns>
    public async Task<LatticeCommit[]> GetHistoryAsync(string userId, int? limit = null)
    {
        var effectiveLimit = limit ?? _defaultHistoryLimit;
        var history = new List<LatticeCommit>();
        var current = await GetHeadAsync(userId);
        while (current != null && history.Count < effectiveLimit)
        {
            var commit = await GetBlobAsync<LatticeCommit>(current);
            history.Add(commit);
            current = string.IsNullOrEmpty(commit.ParentCommitCid) ? null : Cid.FromString(commit.ParentCommitCid);
        }
        return [.. history];
    }

/// <summary>
    /// Gets all nodes of a specific type from the store.
    /// Handles legacy format (pre-type wrapper) for backward compatibility.
    /// </summary>
    /// <typeparam name="T">The node type to filter by.</typeparam>
    /// <returns>All nodes matching the requested type.</returns>
    /// <remarks>
    /// This projection drops the key each node is filed under and any payload that failed to
    /// decode. Verification uses <see cref="ScanBlobsAsync{T}"/> so those two facts survive.
    /// </remarks>
    public async Task<IEnumerable<T>> GetAllNodesAsync<T>() where T : IMerkleNode
    {
        var scan = await ScanBlobsAsync<T>();
        return scan.Nodes.Select(stored => stored.Node);
    }

    /// <summary>
    /// Scans the store for nodes of a specific type, preserving the key each node is filed
    /// under and reporting keys whose payloads could not be decoded.
    /// </summary>
    /// <typeparam name="T">The node type to project.</typeparam>
    /// <param name="ct">Cancellation token.</param>
    public Task<StoreScan<T>> ScanBlobsAsync<T>(CancellationToken ct = default) where T : IMerkleNode
    {
        var nodes = new List<StoredNode<T>>();
        var unreadable = new List<string>();

        foreach (var kvp in _store)
        {
            ct.ThrowIfCancellationRequested();

            // Only object keys; skip head references, signatures, and session indexes.
            if (!kvp.Key.StartsWith("obj:", StringComparison.Ordinal))
            {
                continue;
            }

            var keyCid = Cid.FromString(kvp.Key["obj:".Length..]);

            if (TryDecode<T>(kvp.Value, keyCid, out var node))
            {
                // The key carries the identifier the payload was written under.
                nodes.Add(new StoredNode<T>(keyCid, node!));
            }
            else if (!IsKnownOtherType<T>(kvp.Value, keyCid))
            {
                // Either a corrupt payload or a legacy blob that no longer parses. Both mean
                // the content is unrecoverable, which a verifier must not treat as absent.
                unreadable.Add(kvp.Key);
            }
        }

        return Task.FromResult(new StoreScan<T>(nodes, unreadable));
    }

    /// <summary>
    /// Attempts to decode a stored blob into <typeparamref name="T"/>, accepting the wrapped
    /// format and the legacy raw payload format.
    /// </summary>
    private static bool TryDecode<T>(byte[] data, Cid keyCid, out T? node) where T : IMerkleNode
    {
        node = default;
        try
        {
            var blob = MessagePackSerializer.Deserialize<StoredBlob>(data);
            var actualType = ResolveType(blob.Type);
            if (actualType is not null)
            {
                if (!typeof(T).IsAssignableFrom(actualType))
                {
                    return false;
                }

                node = MessagePackSerializer.Deserialize<T>(blob.Payload);
                return node is not null;
            }

            // An unresolvable type name may just be a legacy payload that happens to look like
            // a wrapper, so fall through rather than rejecting it here.
        }
        catch
        {
            // Fall through to the legacy raw payload format.
        }

        try
        {
            // Legacy format: direct serialization without the type wrapper. It carries no type
            // tag, so accept it only when its content hashes to the key it is filed under;
            // otherwise it is corrupt or belongs to another legacy type.
            var candidate = MessagePackSerializer.Deserialize<T>(data);
            if (candidate is null ||
                !string.Equals(candidate.GetCid().ToString(), keyCid.ToString(), StringComparison.Ordinal))
            {
                return false;
            }

            node = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// True when the blob decodes as a known type that simply is not the type being scanned.
    /// A payload of the requested type that fails to decode is therefore reported as unreadable
    /// rather than mistaken for an unrelated blob.
    /// </summary>
    private static bool IsKnownOtherType<T>(byte[] data, Cid keyCid) where T : IMerkleNode
    {
        try
        {
            var blob = MessagePackSerializer.Deserialize<StoredBlob>(data);
            var actualType = ResolveType(blob.Type);
            if (actualType is not null)
            {
                return !typeof(T).IsAssignableFrom(actualType);
            }
        }
        catch
        {
            // Not a wrapped blob; fall through to the legacy raw format.
        }

        // Legacy raw payloads carry no type tag, so identify them by trying the other known
        // node types. Requiring an identifier match avoids mistaking a damaged payload of the
        // requested type for a different type.
        foreach (var candidate in AllowedTypes.Values)
        {
            if (!typeof(IMerkleNode).IsAssignableFrom(candidate) || typeof(T).IsAssignableFrom(candidate))
            {
                continue;
            }

            try
            {
                if (MessagePackSerializer.Deserialize(candidate, data.AsMemory()) is IMerkleNode node &&
                    string.Equals(node.GetCid().ToString(), keyCid.ToString(), StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch
            {
                // Not this type.
            }
        }

        return false;
    }

    /// <summary>
    /// Stores a typed object and returns its computed CID.
    /// Wraps the payload in a StoredBlob with type information.
    /// </summary>
    /// <typeparam name="T">The object type.</typeparam>
    /// <param name="obj">The object to store.</param>
    /// <returns>The CID of the stored object.</returns>
    public async Task<Cid> PutAsync<T>(T obj)
    {
        var payload = MessagePackSerializer.Serialize(obj);
        var cid = Cid.Compute(payload);
        var key = Key(cid);
        var blob = new StoredBlob(typeof(T).AssemblyQualifiedName ?? typeof(T).FullName ?? string.Empty, payload);
        var data = MessagePackSerializer.Serialize(blob);

        await _lock.WaitAsync();
        try
        {
            _store[key] = data;
            await AppendToDiskAsync(key, data);
        }
        finally
        {
            _lock.Release();
        }

        return cid;
    }

    /// <summary>
    /// Retrieves a typed object by CID with strict type checking.
    /// </summary>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <param name="cid">The content identifier.</param>
    /// <returns>The deserialized object.</returns>
    /// <exception cref="KeyNotFoundException">When CID is not found.</exception>
    /// <exception cref="InvalidCastException">When stored type doesn't match requested type.</exception>
    public async Task<T> GetAsync<T>(Cid cid)
    {
        var key = Key(cid);
        if (!_store.TryGetValue(key, out var data))
        {
            throw new KeyNotFoundException($"CID {cid} not found");
        }

        var blob = MessagePackSerializer.Deserialize<StoredBlob>(data);
        if (ResolveType(blob.Type) is { } actualType && typeof(T).IsAssignableFrom(actualType))
        {
            return MessagePackSerializer.Deserialize<T>(blob.Payload);
        }

        throw new InvalidCastException($"Stored type {blob.Type} is not in the allowed types whitelist or is not compatible with requested type {typeof(T)}");
    }

    /// <summary>
    /// Gets all stored typed objects of a specific type.
    /// </summary>
    /// <typeparam name="T">The object type to filter by.</typeparam>
    /// <returns>All objects matching the requested type.</returns>
    public Task<IEnumerable<T>> GetAllAsync<T>()
    {
        var items = new List<T>();
        foreach (var kvp in _store)
        {
            if (!kvp.Key.StartsWith("obj:", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var blob = MessagePackSerializer.Deserialize<StoredBlob>(kvp.Value);
                if (ResolveType(blob.Type) is { } actualType && typeof(T).IsAssignableFrom(actualType))
                {
                    var item = MessagePackSerializer.Deserialize<T>(blob.Payload);
                    if (item != null) items.Add(item);
                }
            }
            catch
            {
                // Ignore parsing errors for non-matching types
            }
        }
        return Task.FromResult<IEnumerable<T>>(items);
    }

    /// <summary>
    /// Disposes the store asynchronously (no-op in current implementation).
    /// </summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Persists a companion signature blob, retrievable by the signed node's CID.
    /// </summary>
    /// <param name="signature">The signature to persist.</param>
    public async Task PutSignatureAsync(AgentSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        if (string.IsNullOrEmpty(signature.SignedCid))
        {
            throw new ArgumentException("Signature must reference a SignedCid", nameof(signature));
        }

        var key = SignatureKey(Cid.FromString(signature.SignedCid));
        var data = MessagePackSerializer.Serialize(signature);

        await _lock.WaitAsync();
        try
        {
            _store[key] = data;
            await AppendToDiskAsync(key, data);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Retrieves the companion signature for a node, if one exists.
    /// </summary>
    /// <param name="signedCid">The CID of the signed node.</param>
    /// <returns>The signature, or null when the node was stored unsigned.</returns>
    public Task<AgentSignature?> GetSignatureAsync(Cid signedCid)
    {
        var key = SignatureKey(signedCid);
        if (_store.TryGetValue(key, out var data))
        {
            return Task.FromResult<AgentSignature?>(MessagePackSerializer.Deserialize<AgentSignature>(data));
        }
        return Task.FromResult<AgentSignature?>(null);
    }

    /// <summary>
    /// Clears all data from the store: in-memory dictionary, persisted store.dat,
    /// log directory, and checkpoints directory.
    /// </summary>
    public async Task ClearAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _store.Clear();
            _appendedLines = 0;

            var dataFile = Path.Combine(_baseDir, "store.dat");
            if (File.Exists(dataFile))
                File.Delete(dataFile);

            var logDir = Path.Combine(_baseDir, "log");
            if (Directory.Exists(logDir))
                Directory.Delete(logDir, true);

            var checkDir = Path.Combine(_baseDir, "checkpoints");
            if (Directory.Exists(checkDir))
                Directory.Delete(checkDir, true);

            Directory.CreateDirectory(logDir);
            Directory.CreateDirectory(checkDir);
        }
        finally
        {
            _lock.Release();
        }

        await Task.CompletedTask;
    }
}
