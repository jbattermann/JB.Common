using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;

namespace JB.Collections
{
    /// <summary>
    ///     An <see cref="IBindingList" /> implementation that's synchronized via an underlying lock-based
    ///     <see cref="IList{T}" />
    ///     implementation.
    /// </summary>
    /// <typeparam name="T">The type of elements in the list.</typeparam>
    public class SynchronizedBindingList<T> : EnhancedBindingList<T>, ICollection
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="SynchronizedBindingList{T}" /> class.
        /// </summary>
        /// <param name="list">
        ///     An <see cref="T:System.Collections.Generic.IList`1" /> of items to be contained in the
        ///     <see cref="T:System.ComponentModel.BindingList`1" />.
        /// </param>
        /// <param name="syncRoot">The object used to synchronize access to the thread-safe collection.</param>
        public SynchronizedBindingList(IList<T> list = null, object syncRoot = null)
            : base(syncRoot == null ? new SynchronizedCollection<T>(list ?? new List<T>()) : new SynchronizedCollection<T>(syncRoot, list ?? new List<T>()))
        {
        }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is synchronized.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is synchronized; otherwise, <c>false</c>.
        /// </value>
        bool ICollection.IsSynchronized => true;

        /// <summary>
        /// Gets an object that can be used to synchronize access to the <see cref="T:System.Collections.ICollection"/>.
        /// </summary>
        /// 
        /// <returns>
        /// An object that can be used to synchronize access to the <see cref="T:System.Collections.ICollection"/>.
        /// </returns>
        object ICollection.SyncRoot => (Items as SynchronizedCollection<T>)?.SyncRoot;
    }
}