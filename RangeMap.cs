using System;
using System.Collections.Generic;
using System.DataStructures;
using System.Linq;
using System.Text;

namespace ZfsSharp
{
    class RangeMap
    {
        private readonly AvlTree<SpaceRange> mTree = new AvlTree<SpaceRange>();

        public RangeMap()
        {
        }

        public void AddRange(ulong offset, ulong range)
        {
            ValidateParams(offset, range);

            if (mTree.Count == 0)
            {
                mTree.Add(new SpaceRange(offset, range));
                return;
            }

            var newRange = new SpaceRange(offset, range);

            var near = mTree.FindNearestValues(newRange);
            if (near.Item1 != null)
            {
                if (near.Item1.Intersects(newRange))
                    throw new Exception("Range already added");
                if (near.Item1.Offset + near.Item1.Range == newRange.Offset)
                {
                    if (!mTree.Remove(near.Item1))
                        throw new Exception("Failed to remove node, this should not happen.");
                    newRange = new SpaceRange(near.Item1.Offset, near.Item1.Range + newRange.Range);
                }
            }
            else if (near.Item2 != null)
            {
                if (near.Item2.Intersects(newRange))
                    throw new Exception("Range already added.");
                if (newRange.Offset + newRange.Range == near.Item2.Offset)
                {
                    if (!mTree.Remove(near.Item2))
                        throw new Exception("Failed to remove node, this should not happen.");
                    newRange = new SpaceRange(newRange.Offset, newRange.Range + near.Item2.Range);
                }
            }

            mTree.Add(newRange);
        }

        public void RemoveRange(ulong offset, ulong range)
        {
            ValidateParams(offset, range);

            if (mTree.Count == 0)
                throw new Exception("Empty tree.");

            var removeRange = new SpaceRange(offset, range);

            var near = mTree.FindNearestValues(removeRange);

            if (near.Item1 != null && near.Item1.Contains(removeRange))
            {
                SplitNode(near.Item1, removeRange);
                return;
            }
            else if (near.Item2 != null && near.Item2.Contains(removeRange))
            {
                SplitNode(near.Item2, removeRange);
                return;
            }

            throw new Exception("Could not find range to remove.");
        }

        private void SplitNode(SpaceRange existingRange, SpaceRange removeRange)
        {
            var newLowerRange = new SpaceRange(existingRange.Offset, removeRange.Offset - existingRange.Offset);
            var newUpperRange = new SpaceRange(removeRange.Offset + removeRange.Range, existingRange.Range - newLowerRange.Range - removeRange.Range);
            if (!mTree.Remove(existingRange))
                throw new Exception("Failed to remove range.");
            if (newLowerRange.Range != 0)
                mTree.Add(newLowerRange);
            if (newUpperRange.Range != 0)
                mTree.Add(newUpperRange);
        }

        void ValidateParams(ulong offset, ulong range)
        {
            if (range == 0)
                throw new ArgumentOutOfRangeException("range", "Range cannot be zero.");
            if (offset > (offset + range))
                throw new OverflowException();
        }

        public bool ContainsRange(ulong offset, ulong range)
        {
            if (mTree.Count == 0)
                return false;
            var search = new SpaceRange(offset, range);
            var near = mTree.FindNearestValues(search);
            return (near.Item1 != null && near.Item1.Contains(search)) || (near.Item2 != null && near.Item2.Contains(search));
        }

        public void Print()
        {
            foreach (var sr in mTree.GetInorderEnumerator())
            {
                Console.WriteLine(sr);
            }
            Console.WriteLine();
        }

        class SpaceRange : IComparable<SpaceRange>
        {
            public readonly ulong Offset;
            public readonly ulong Range;

            public SpaceRange(ulong offset, ulong range)
            {
                this.Offset = offset;
                this.Range = range;
            }

            public int CompareTo(SpaceRange other)
            {
                if (this.Offset < other.Offset)
                    return -1;
                if (this.Offset > other.Offset)
                    return 1;
                return 0;
            }

            public bool Intersects(SpaceRange other)
            {
                ulong myEnd = this.Offset + this.Range;
                if (other.Offset >= this.Offset && other.Offset < myEnd)
                    return true;
                ulong otherEnd = other.Offset + other.Range;
                if (otherEnd > this.Offset && otherEnd <= myEnd)
                    return true;

                if (other.Offset < this.Offset && otherEnd > myEnd)
                    return true;

                return false;
            }

            public bool Contains(SpaceRange other)
            {
                ulong myEnd = this.Offset + this.Range;
                ulong otherEnd = other.Offset + other.Range;
                return other.Offset >= this.Offset && otherEnd <= myEnd;
            }

            public override string ToString()
            {
                return string.Format("{{ {0:x}, {1:x} }}", Offset, Range);
            }

            private static void TestIntersects()
            {
                var space = new SpaceRange(100, 100);

                Console.WriteLine("should be true:");
                Console.WriteLine(space.Intersects(new SpaceRange(150, 25)));
                Console.WriteLine(space.Intersects(new SpaceRange(100, 25)));
                Console.WriteLine(space.Intersects(new SpaceRange(50, 200)));
                Console.WriteLine(space.Intersects(new SpaceRange(190, 10)));

                Console.WriteLine(space.Intersects(new SpaceRange(90, 20)));
                Console.WriteLine(space.Intersects(new SpaceRange(190, 20)));

                Console.WriteLine();
                Console.WriteLine("should be false:");
                Console.WriteLine(space.Intersects(new SpaceRange(50, 25)));
                Console.WriteLine(space.Intersects(new SpaceRange(90, 10)));
                Console.WriteLine(space.Intersects(new SpaceRange(200, 10)));
            }
        }
    }


}
