using System;
using System.Linq;

namespace Utilities
{
    public abstract class EqualityBase<T> : IEquatable<EqualityBase<T>>
        where T : class
    {
        private const int MaxStackDepth = 100;
        private int stackDepth = 0;

        /// <summary>
        /// Determines whether the specified <see href="T"/> is equal to the current instance.
        /// </summary>
        /// <param name="otherT">The other instance to compare with the current instance . </param>
        /// <returns>true if the specified instance is equal to the current instance; otherwise, false.</returns>
        public bool Equals(EqualityBase<T> otherT)
        {
            if (otherT is null)
            {
                return false;
            }

            if (this.GetType() != otherT.GetType())
            {
                return false;
            }

            if (this.stackDepth > MaxStackDepth)
            {
                throw new InvalidOperationException($"Class {typeof(T)} inherits from EqualityBase but its equatable values seem to be recursive.");
            }

            bool result;
            this.stackDepth++;
            try
            {
                result = Enumerable.SequenceEqual(this.GetEquatableValues(), otherT.GetEquatableValues());
            }
            finally
            {
                this.stackDepth--;
            }

            return result;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return this.Equals(obj as EqualityBase<T>);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            if (this.stackDepth > MaxStackDepth)
            {
                throw new InvalidOperationException($"Class {typeof(T)} inherits from EqualityBase but its equatable values seem to be recursive.");
            }

            int result;
            this.stackDepth++;
            try
            {
                result = EqualityBase<T>.CombineHashCodes(this.GetEquatableValues());
            }
            finally
            {
                this.stackDepth--;
            }

            return result;
        }

        public static bool operator ==(EqualityBase<T> a, EqualityBase<T> b)
        {
            if (a is null)
            {
                return b is null;
            }

            return a.Equals(b);
        }

        public static bool operator !=(EqualityBase<T> a, EqualityBase<T> b)
        {
            return !(a == b);
        }

        private static int CombineHashCodes(params object[] values)
        {
            if (values == null || values.Length <= 0)
            {
                throw new ArgumentNullException(nameof(values));
            }

            int hashCode = 0;

            for (int i = 0; i < values.Length; i++)
            {
                hashCode = CombineHashCodes(hashCode, values[i]);
            }

            return hashCode;
        }

        private static int CombineHashCodes(int hashCode, object value)
        {
            if (value == null)
            {
                return hashCode;
            }

            int hashCode2 = value.GetHashCode();
            return hashCode ^ hashCode2;
        }

        /// <summary>
        /// Gets the values to be used for equality comparisons and hash code generation
        /// </summary>
        /// <returns>A collection of values to be used</returns>
        protected abstract object[] GetEquatableValues();
    }
}
