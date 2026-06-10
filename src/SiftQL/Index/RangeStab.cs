namespace SiftQL.Index;

// Binary-search helpers for interval stabbing over sorted bound arrays.
internal static class RangeStab
{
    // Count of keys[i] <= x, i.e. the exclusive upper index of the candidate
    // prefix in an ascending array of lower bounds.
    public static int CountLessOrEqual(decimal[] keys, decimal x)
    {
        int lo = 0;
        int hi = keys.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (keys[mid] <= x)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }

    // First index where keys[i] >= x, i.e. the inclusive start of the candidate
    // suffix in an ascending array of upper bounds.
    public static int FirstGreaterOrEqual(decimal[] keys, decimal x)
    {
        int lo = 0;
        int hi = keys.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (keys[mid] < x)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }
}
