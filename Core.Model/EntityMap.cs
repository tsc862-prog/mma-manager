using System.Collections;

namespace MmaManager.Core.Model;

/// <summary>An entity id: a strongly-typed wrapper over the int the <see cref="World"/> allocates monotonically.</summary>
public interface IEntityId
{
    int Value { get; }
}

/// <summary>
/// The world's entity collections (fighters, events, promotions...). A hash map with O(1) lookup
/// whose enumeration is ALWAYS in ascending id order, whatever the history of adds and removes and
/// whether the world was loaded from a save.
/// <para>
/// Why not a plain <see cref="Dictionary{TKey,TValue}"/>: it enumerates in insertion order only until
/// something is removed; the next add reuses the freed slot, so a world that cancelled an event (or
/// released a local) enumerated differently from the same world saved and loaded (which is rebuilt
/// compact). The sim iterates these collections while drawing from shared RNG streams, so enumeration
/// order fed rolls and the two worlds diverged. Here enumeration order is a pure function of the set of
/// ids present.
/// </para>
/// <para>
/// How: the backing dictionary is kept in id order. Ids are allocated monotonically, so the normal add
/// (a key above every key present, into a map with no freed slots) is a plain append. A remove marks the
/// map as holding freed slots; the next add, or any add below the highest key, rebuilds the backing
/// dictionary compact and sorted (O(n log n), and rare: removals only happen on the player's cancel and
/// release paths). Enumeration and lookup run straight on the backing dictionary.
/// </para>
/// </summary>
public sealed class EntityMap<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>, IDictionary
    where TKey : struct, IEntityId
{
    private Dictionary<TKey, TValue> _map = new();
    private int _maxKey = int.MinValue;   // an upper bound on the keys present (stale-high after a remove is fine)
    private bool _holes;                  // a remove left a freed slot that the next Dictionary add would reuse

    public int Count => _map.Count;

    public TValue this[TKey key]
    {
        get => _map[key];
        set
        {
            if (_map.ContainsKey(key)) _map[key] = value;   // replacing in place keeps the position
            else Add(key, value);
        }
    }

    /// <summary>Insert a new entity; throws if the id is already present (like <see cref="Dictionary{TKey,TValue}.Add"/>).</summary>
    public void Add(TKey key, TValue value)
    {
        if (_map.ContainsKey(key)) throw new ArgumentException($"An entity with id {key} is already present.", nameof(key));
        if (_holes || key.Value < _maxKey)
        {
            Rebuild(key, value);
            return;
        }
        _map.Add(key, value);
        _maxKey = key.Value;
    }

    public bool Remove(TKey key)
    {
        if (!_map.Remove(key)) return false;
        _holes = true;   // the freed slot would be reused by the next Dictionary add, out of id order
        return true;
    }

    public void Clear()
    {
        _map.Clear();
        _holes = false;
        _maxKey = int.MinValue;
    }

    // Compact and sort, with one extra entry. Nothing in the old backing map is reused, so no freed slot survives.
    private void Rebuild(TKey extraKey, TValue extraValue)
    {
        var entries = new List<KeyValuePair<TKey, TValue>>(_map.Count + 1);
        foreach (var kv in _map) entries.Add(kv);
        entries.Add(new KeyValuePair<TKey, TValue>(extraKey, extraValue));
        entries.Sort(static (a, b) => a.Key.Value.CompareTo(b.Key.Value));
        var fresh = new Dictionary<TKey, TValue>(entries.Count);
        foreach (var kv in entries) fresh.Add(kv.Key, kv.Value);
        _map = fresh;
        _holes = false;
        _maxKey = entries[^1].Key.Value;
    }

    public bool ContainsKey(TKey key) => _map.ContainsKey(key);
    public bool TryGetValue(TKey key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out TValue value) => _map.TryGetValue(key, out value);
    public TValue? GetValueOrDefault(TKey key) => _map.TryGetValue(key, out var v) ? v : default;

    /// <summary>The entities, in ascending id order.</summary>
    public Dictionary<TKey, TValue>.ValueCollection Values => _map.Values;

    /// <summary>The ids, ascending.</summary>
    public Dictionary<TKey, TValue>.KeyCollection Keys => _map.Keys;

    public Dictionary<TKey, TValue>.Enumerator GetEnumerator() => _map.GetEnumerator();

    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => _map.Keys;
    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => _map.Values;
    IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => _map.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _map.GetEnumerator();

    // Non-generic IDictionary, so reflection walkers (the save round-trip comparer) see a dictionary.
    // Every mutation routes through the ordered paths above.
    bool IDictionary.IsFixedSize => false;
    bool IDictionary.IsReadOnly => false;
    ICollection IDictionary.Keys => _map.Keys;
    ICollection IDictionary.Values => _map.Values;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => ((ICollection)_map).SyncRoot;
    object? IDictionary.this[object key]
    {
        get => ((IDictionary)_map)[key];
        set => this[(TKey)key] = (TValue)value!;
    }
    void IDictionary.Add(object key, object? value) => Add((TKey)key, (TValue)value!);
    bool IDictionary.Contains(object key) => key is TKey k && _map.ContainsKey(k);
    IDictionaryEnumerator IDictionary.GetEnumerator() => ((IDictionary)_map).GetEnumerator();
    void IDictionary.Remove(object key) { if (key is TKey k) Remove(k); }
    void ICollection.CopyTo(Array array, int index) => ((ICollection)_map).CopyTo(array, index);
}
