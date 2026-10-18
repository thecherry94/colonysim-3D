namespace ColonySim.Core;

/// <summary>Fog-of-war discovery: non-solid cells connected to the sky (or to a dug cell) become known.</summary>
public static class Discovery
{
    public static void InitialFlood(VoxelWorld w)
    {
        int sx = w.SizeX, sz = w.SizeZ, top = w.SizeY - 1;
        var solid = SolidTable();
        var queue = new IntQueue(1 << 16);
        var low = new int[sx * sz]; // lowest sky-connected y of each column (top+1 if the top cell is solid)

        // Pass 1: sky columns, walking down until the first solid block.
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
            {
                int y = top;
                while (y >= 0 && !solid[w.GetBlock(x, y, z)]) { w.SetDiscovered(x, y, z, false); y--; }
                low[z * sx + x] = y + 1;
            }

        // Pass 2: seed the BFS with sky cells that can touch undiscovered cells, i.e. those below a neighboring column's sky floor.
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
            {
                int lo = low[z * sx + x];
                int hi = lo;
                if (x > 0) hi = Math.Max(hi, low[z * sx + x - 1]);
                if (x < sx - 1) hi = Math.Max(hi, low[z * sx + x + 1]);
                if (z > 0) hi = Math.Max(hi, low[(z - 1) * sx + x]);
                if (z < sz - 1) hi = Math.Max(hi, low[(z + 1) * sx + x]);
                if (hi > top + 1) hi = top + 1;
                // The cell at lo - 1 is solid (or lo == 0), so seeding starts at lo.
                for (int y = lo; y < hi; y++) queue.Push((y * sz + z) * sx + x);
            }

        Flood(w, solid, queue, markDirty: false);
    }

    public static int RevealFrom(VoxelWorld w, int x, int y, int z)
    {
        if (!w.InBounds(x, y, z)) return 0;
        var solid = SolidTable();
        if (solid[w.GetBlock(x, y, z)]) return 0;
        int count = 0;
        if (!w.IsDiscovered(x, y, z)) { w.SetDiscovered(x, y, z); count++; }
        var queue = new IntQueue(1024);
        queue.Push((y * w.SizeZ + z) * w.SizeX + x);
        return count + Flood(w, solid, queue, markDirty: true);
    }

    static int Flood(VoxelWorld w, bool[] solid, IntQueue queue, bool markDirty)
    {
        int sx = w.SizeX, sy = w.SizeY, sz = w.SizeZ, found = 0;
        while (queue.TryPop(out int p))
        {
            int x = p % sx, r = p / sx, z = r % sz, y = r / sz;
            if (x > 0) Visit(x - 1, y, z);
            if (x < sx - 1) Visit(x + 1, y, z);
            if (z > 0) Visit(x, y, z - 1);
            if (z < sz - 1) Visit(x, y, z + 1);
            if (y > 0) Visit(x, y - 1, z);
            if (y < sy - 1) Visit(x, y + 1, z);
        }
        return found;

        void Visit(int nx, int ny, int nz)
        {
            if (solid[w.GetBlock(nx, ny, nz)] || w.IsDiscovered(nx, ny, nz)) return;
            w.SetDiscovered(nx, ny, nz, markDirty);
            found++;
            queue.Push((ny * sz + nz) * sx + nx);
        }
    }

    static bool[] SolidTable()
    {
        var defs = Blocks.Registry.All;
        var t = new bool[defs.Count];
        for (int i = 0; i < t.Length; i++) t[i] = defs[i].IsSolid;
        return t;
    }

    /// <summary>FIFO ring buffer of packed ints that doubles when full.</summary>
    sealed class IntQueue
    {
        int[] _a; int _head, _count;
        public IntQueue(int capacity) => _a = new int[capacity];

        public void Push(int v)
        {
            if (_count == _a.Length) Grow();
            _a[(_head + _count) & (_a.Length - 1)] = v;
            _count++;
        }

        public bool TryPop(out int v)
        {
            if (_count == 0) { v = 0; return false; }
            v = _a[_head];
            _head = (_head + 1) & (_a.Length - 1);
            _count--;
            return true;
        }

        void Grow()
        {
            var n = new int[_a.Length * 2];
            for (int i = 0; i < _count; i++) n[i] = _a[(_head + i) & (_a.Length - 1)];
            _a = n; _head = 0;
        }
    }
}
