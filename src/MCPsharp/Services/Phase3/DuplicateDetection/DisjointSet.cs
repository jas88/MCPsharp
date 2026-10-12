namespace MCPsharp.Services.Phase3.DuplicateDetection;

/// <summary>
/// Union-find over integer indices with path compression and union by size
/// </summary>
internal sealed class DisjointSet
{
    private readonly int[] _parent;
    private readonly int[] _size;

    public DisjointSet(int count)
    {
        _parent = new int[count];
        _size = new int[count];
        for (var i = 0; i < count; i++)
        {
            _parent[i] = i;
            _size[i] = 1;
        }
    }

    public int Find(int index)
    {
        var root = index;
        while (_parent[root] != root)
        {
            root = _parent[root];
        }

        while (_parent[index] != root)
        {
            var next = _parent[index];
            _parent[index] = root;
            index = next;
        }

        return root;
    }

    public void Union(int first, int second)
    {
        var rootA = Find(first);
        var rootB = Find(second);
        if (rootA == rootB)
        {
            return;
        }

        if (_size[rootA] < _size[rootB])
        {
            (rootA, rootB) = (rootB, rootA);
        }

        _parent[rootB] = rootA;
        _size[rootA] += _size[rootB];
    }
}
