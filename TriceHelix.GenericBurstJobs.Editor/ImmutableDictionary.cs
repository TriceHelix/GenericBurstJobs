using System.Collections.Generic;

namespace TriceHelix.GenericBurstJobs.Editor
{
    internal class ImmutableMultiDictionary<TKey, TValue>
    {
        private readonly Dictionary<TKey, int> KeyToValueIndex;
        private readonly TValue[] Values;
        private readonly int[] Links;


        internal ImmutableMultiDictionary(KeyValuePair<TKey, TValue>[] keyValuePairs)
        {
            int len = keyValuePairs != null ? keyValuePairs.Length : 0;
            KeyToValueIndex = new(len);
            Values = new TValue[len];
            Links = new int[len];

            for (int i = 0; i < len; i++)
            {
                ref var kv = ref keyValuePairs[i];
                if (KeyToValueIndex.TryGetValue(kv.Key, out int next)) // existing key
                {
                    // get last link in chain
                    int linkIndex;
                    do
                    {
                        next--;
                        linkIndex = next;
                        next = Links[next];
                    }
                    while (next > 0);

                    // link last element to this one
                    Links[linkIndex] = i + 1;
                }
                else // new key
                {
                    KeyToValueIndex.Add(kv.Key, i + 1);
                }

                // insert value
                Values[i] = kv.Value;
            }
        }


        internal IEnumerable<TValue> GetValuesForKey(TKey key)
        {
            if (!KeyToValueIndex.TryGetValue(key, out int index))
                yield break;

            do
            {
                index--;
                yield return Values[index];
                index = Links[index];
            }
            while (index > 0);
        }
    }
}
