using System;
using System.Collections.Generic;

namespace RTMaquetaXR
{
    // The complete native catalogue is retained. A page exposes at most ten
    // actions, leaving two large, explicit navigation sectors on the same aro.
    internal sealed class TouchRadialPage
    {
        internal int Start, Count;
        internal string Section;
        internal bool OwnSection;
    }
    internal static class TouchRadialPagePolicy
    {
        internal const int ActionCapacity = TouchRadialPolicy.RingCapacity - 2;
        internal static List<TouchRadialPage> Build(IList<string> sections, IList<bool> own)
        {
            var pages = new List<TouchRadialPage>();
            for (int at = 0; at < sections.Count;)
            {
                int count = 1;
                while (at + count < sections.Count && count < ActionCapacity && sections[at + count] == sections[at] && own[at + count] == own[at]) ++count;
                pages.Add(new TouchRadialPage { Start = at, Count = count, Section = sections[at], OwnSection = own[at] });
                at += count;
            }
            return pages;
        }
        internal static int Move(int page, int delta, int count) => count <= 0 ? 0 : ((page + delta) % count + count) % count;
    }
}
