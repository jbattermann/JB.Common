using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive;

namespace JB.Collections
{
    public interface INotifyReactiveCollectionChanged<out T> : INotifyCollectionChanged
	{
		/// <summary>
		/// (Temporarily) suppresses change notifications until the returned <see cref="IDisposable" />
		/// has been Disposed and a Reset will be signaled.
		/// </summary>
		/// <param name="signalResetWhenFinished">if set to <c>true</c> signals a reset when finished.</param>
		/// <returns></returns>
		IDisposable SuppressReactiveCollectionChangedNotifications(bool signalResetWhenFinished = true);

        /// <summary>
        /// Gets a value indicating whether this instance is currently suppressing reactive collection changed notifications.
        /// </summary>
        /// <value>
        /// <c>true</c> if this instance is suppressing reactive collection changed notifications; otherwise, <c>false</c>.
        /// </value>
        bool IsSuppressingReactiveCollectionChangedNotifications { get; }

        /// <summary>
        /// Gets a value indicating whether this instance has per item change tracking enabled and therefore listens to <typeparam name="T"/>'s <see cref="INotifyPropertyChanged.PropertyChanged"/> events, if the interface is implemented.
        /// </summary>
        /// <value>
        /// <c>true</c> if this instance has item change tracking enabled; otherwise, <c>false</c>.
        /// </value>
        bool IsItemChangeTrackingEnabled { get; }

        /// <summary>
        /// Indicates at what percentage / fraction bulk changes are signaled as a Reset rather than individual change()s.
        /// [0] = Always, [1] = Never.
        /// </summary>
        /// <value>
        /// The changes to reset threshold.
        /// </value>
        double ItemChangesToResetThreshold { get; }

        /// <summary>
        /// Gets the minimum amount of items that have been changed to be notified / considered a <see cref="ReactiveCollectionChangeType.Reset"/> rather than indivudal <see cref="ReactiveCollectionChangeType"/> notifications.
        /// </summary>
        /// <value>
        /// The minimum items changed to be considered reset.
        /// </value>
        int MinimumItemsChangedToBeConsideredReset { get; set; }

        /// <summary>
        /// Gets the collection change notifications as an observable stream.
        /// </summary>
        /// <value>
        /// The collection changes.
        /// </value>
        IObservable<IReactiveCollectionChange<T>> CollectionChanges { get; }

		/// <summary>
		/// Gets the count change notifications as an observable stream.
		/// </summary>
		/// <value>
		/// The count changes.
		/// </value>
		IObservable<int> CountChanges { get; }

		/// <summary>
		/// Gets the reset notifications as an observable stream.  Whenever signaled,
		/// observers should reset any knowledge / state etc about the list.
		/// </summary>
		/// <value>
		/// The resets.
		/// </value>
		IObservable<Unit> Resets { get; }
	}
}