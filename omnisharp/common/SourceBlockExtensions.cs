using System.Collections.Generic;
using System.Threading.Tasks.Dataflow;

namespace FastCodeNavPlugin.Common
{
    public static class SourceBlockExtensions
    {
        public static IList<T> ReceiveAll<T>(this IReceivableSourceBlock<T> buffer)
        {
            IList<T> receivedItems = new List<T>();
            T receivedItem = default(T);
            while (buffer.TryReceive(out receivedItem))
            {
                receivedItems.Add(receivedItem);
            }
            return receivedItems;
        }
    }
}
