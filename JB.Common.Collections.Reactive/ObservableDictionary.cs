using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Threading;
using JB.Collections.Reactive.ExtensionMethods;
using JB.Reactive;
using JB.Reactive.Linq;

namespace JB.Collections.Reactive
{
    /// <summary>
    /// Represents a thread-safe collection of key/value pairs that can be accessed by multiple threads concurrently
    /// that notifies subscribers about changes to its containing list of key/value pairs.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of the values in the dictionary.</typeparam>
    [DebuggerDisplay("Count={Count}")]
    public class ObservableDictionary<TKey, TValue> :
        IObservableDictionary<TKey, TValue>,
        IBulkModifiableDictionary<TKey, TValue>,
        IObservableReadOnlyDictionary<TKey, TValue>,
        IObservableReadOnlyCollection<KeyValuePair<TKey, TValue>>,
        IObservableCollection<KeyValuePair<TKey, TValue>>,
        ICollection, IDisposable
    {
        private const string ItemIndexerName = "Item[]"; // taken from ObservableCollection.cs Line #421

        private IDisposable _dictionaryChangesItemIndexerPropertyChangedForwarder;
        private IDisposable _countChangesCountPropertyChangedForwarder;

        private Subject<int> _countChangesSubject = new Subject<int>();
        private Subject<ObserverException> _unhandledObserverExceptionsSubject = new Subject<ObserverException>();
        private Subject<IObservableDictionaryChange<TKey, TValue>> _dictionaryChangesSubject = new Subject<IObservableDictionaryChange<TKey, TValue>>();

        /// <summary>
        /// Gets the count changes observer.
        /// </summary>
        /// <value>
        /// The count changes observer.
        /// </value>
        protected IObserver<int> CountChangesObserver { get; private set; }

        /// <summary>
        /// Gets the thrown exceptions observer.
        /// </summary>
        /// <value>
        /// The thrown exceptions observer.
        /// </value>
        protected IObserver<ObserverException> ObserverExceptionsObserver { get; private set; }

        /// <summary>
        /// Gets the dictionary changes observer.
        /// </summary>
        /// <value>
        /// The dictionary changes observer.
        /// </value>
        protected IObserver<IObservableDictionaryChange<TKey, TValue>> DictionaryChangesObserver { get; private set; }

        /// <summary>
        /// Gets the actual dictionary used - the rest in here is just fancy wrapping paper.
        /// </summary>
        /// <value>
        /// The inner dictionary.
        /// </value>
        protected ConcurrentDictionary<TKey, TValue> InnerDictionary { get; }

        /// <summary>
        ///     Gets the scheduler used to schedule observer notifications and where events are raised on.
        /// </summary>
        /// <value>
        ///     The scheduler.
        /// </value>
        protected IScheduler Scheduler { get; }

        /// <summary>
        /// Gets the <see cref="IEqualityComparer{T}" /> implementation to use when comparing keys.
        /// </summary>
        /// <value>
        /// The <typeparamref name="TKey"/> comparer.
        /// </value>
        protected IEqualityComparer<TKey> KeyComparer { get; }

        /// <summary>
        /// Gets the <see cref="IEqualityComparer{T}" /> implementation to use when comparing values.
        /// </summary>
        /// <value>
        /// The <typeparamref name="TValue"/> comparer.
        /// </value>
        protected IEqualityComparer<TValue> ValueComparer { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:ObservableDictionary" /> class that contains elements
        /// copied from the specified <paramref name="collection" /> and uses the specified
        /// <see cref="T:System.Collections.Generic.IEqualityComparer`1" />.
        /// </summary>
        /// <param name="collection">The elements that are copied to this instance.</param>
        /// <param name="keyComparer">The <see cref="IEqualityComparer{T}" /> implementation to use when comparing keys.</param>
        /// <param name="valueComparer">The <see cref="IEqualityComparer{T}" /> implementation to use when comparing values.</param>
        /// <param name="scheduler">The scheduler to to send out observer messages &amp; raise events on. If none is provided <see cref="System.Reactive.Concurrency.Scheduler.CurrentThread"/> will be used.</param>
        public ObservableDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection = null, IEqualityComparer<TKey> keyComparer = null, IEqualityComparer<TValue> valueComparer = null, IScheduler scheduler = null)
        {
            // ToDo: check whether scheduler shall / should be used for internall used RX notifications / Subjects etc
            Scheduler = scheduler ?? System.Reactive.Concurrency.Scheduler.CurrentThread;

            KeyComparer = keyComparer ?? EqualityComparer<TKey>.Default;
            ValueComparer = valueComparer ?? EqualityComparer<TValue>.Default;

            InnerDictionary = collection != null
                    ? new ConcurrentDictionary<TKey, TValue>(collection, KeyComparer)
                    : new ConcurrentDictionary<TKey, TValue>(KeyComparer);

            ThresholdAmountWhenChangesAreNotifiedAsReset = 100;

            IsTrackingChanges = true;
            IsTrackingItemChanges = true;
            IsTrackingCountChanges = true;
            IsTrackingResets = true;

            // hook up INPC handling for values handed in on .ctor
            foreach (var keyValuePair in InnerDictionary)
            {
                AddKeyToPropertyChangedHandling(keyValuePair.Key);
                AddValueToPropertyChangedHandling(keyValuePair.Value);
            }

            SetupObservablesAndObserversAndSubjects();
        }

        #region Implementation of ObservableDictionary<TKey, TValue>

        /// <summary>
        /// Adds a key/value pair if the key does not already exist, or performs an update by replacing the existing, old value with the new one.
        /// </summary>
        /// <param name="key">The key to be added or whose value should be updated</param>
        /// <param name="value">The value to be added or used to update if the key is already present.</param>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="key"/>is null.</exception>
        /// <exception cref="T:System.OverflowException">The dictionary already contains the maximum number of elements (<see cref="F:System.Int32.MaxValue"/>).</exception>
        public virtual void AddOrUpdate(TKey key, TValue value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            CheckForAndThrowIfDisposed();

            var wasAdded = true;
            TValue oldValueIfReplaced = default(TValue);

            InnerDictionary.AddOrUpdate(key, value, (usedKey, oldValue) =>
            {
                // in case we get in here, which means the inner dictionary did already contain a value for the given key,
                // remove the old value from property changed handling first at any circumstance as INPC hook will be (re-)added
                // in the next step after AddOrUpdate (again)
                wasAdded = false;

                RemoveKeyFromPropertyChangedHandling(key);
                RemoveValueFromPropertyChangedHandling(oldValue);

                // check whether we already have the same value for the given key in the inner dictionary so we signal
                // add or changed/replaced correspondingly
                if (!AreTheSameValue(oldValue, value))
                {
                    oldValueIfReplaced = oldValue;
                }

                return value; // always return the 'new' value here as the old value shall never be kept.
            });

            // hook up INPC handling to the key and value (again)
            AddKeyToPropertyChangedHandling(key);
            AddValueToPropertyChangedHandling(value);

            // signal change to subscribers
            if (wasAdded)
            {
                NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.ItemAdded(this, key, value));
            }
            else
            {
                NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.ItemValueReplaced(this, key, value, oldValueIfReplaced));

            }
        }

        /// <summary>
        /// Adds an element with the provided key and value to the <see cref="T:System.Collections.Generic.IDictionary`2" /> and if successful it optionally signals observers
        /// about the change.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <param name="notifyObserversAboutChange">if set to <c>true</c> observers will be notified about the change.</param>
        protected virtual void Add(TKey key, TValue value, bool notifyObserversAboutChange)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            CheckForAndThrowIfDisposed();

            ((IDictionary<TKey, TValue>)InnerDictionary).Add(key, value);

            AddKeyToPropertyChangedHandling(key);
            AddValueToPropertyChangedHandling(value);

            if (notifyObserversAboutChange)
                NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.ItemAdded(this, key, value));
        }

        /// <summary>
        /// Removes the element with the specified key from the <see cref="T:System.Collections.Generic.IDictionary`2" /> and if successful it optionally signals observers
        /// about the change.
        /// </summary>
        /// <param name="key">The key of the element to remove.</param>
        /// <param name="notifyObserversAboutChange">if set to <c>true</c> observers will be notified about the change.</param>
        /// <returns>
        /// true if the element is successfully removed; otherwise, false.  This method also returns false if <paramref name="key" /> was not found in the original <see cref="T:System.Collections.Generic.IDictionary`2" />.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        ///   <paramref name="key" /> is null.</exception>
        /// <exception cref="T:System.NotSupportedException">The <see cref="T:System.Collections.Generic.IDictionary`2" /> is read-only.</exception>
        protected virtual bool Remove(TKey key, bool notifyObserversAboutChange)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            CheckForAndThrowIfDisposed();

            TValue valueForKey;
            return TryRemove(key, out valueForKey, notifyObserversAboutChange);
        }

        /// <summary>
        /// Attempts to add the specified key and value to the <see cref="ObservableDictionary{TKey,TValue}"/>.
        /// </summary>
        /// 
        /// <returns>
        /// true if the key/value pair was added to the <see cref="ObservableDictionary{TKey,TValue}"/> successfully; false if the key already exists.
        /// </returns>
        /// <param name="key">The key of the element to add.</param><param name="value">The value of the element to add. The value can be  null for reference types.</param>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="key"/> is  null.</exception>
        /// <exception cref="T:System.OverflowException">The dictionary already contains the maximum number of elements (<see cref="F:System.Int32.MaxValue"/>).</exception>
        public virtual bool TryAdd(TKey key, TValue value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            CheckForAndThrowIfDisposed();

            return TryAdd(key, value, IsTrackingChanges);
        }

        /// <summary>
        /// Attempts to add the specified key and value to the <see cref="ObservableDictionary{TKey,TValue}" />.
        /// </summary>
        /// <param name="key">The key of the element to add.</param>
        /// <param name="value">The value of the element to add. The value can be  null for reference types.</param>
        /// <param name="notifyObserversAboutChange">if set to <c>true</c> observers will be notified about the change.</param>
        /// <returns>
        /// true if the key/value pair was added to the <see cref="ObservableDictionary{TKey,TValue}" /> successfully; false if the key already exists.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        ///   <paramref name="key" /> is  null.</exception>
        /// <exception cref="T:System.OverflowException">The dictionary already contains the maximum number of elements (<see cref="F:System.Int32.MaxValue" />).</exception>
        protected virtual bool TryAdd(TKey key, TValue value, bool notifyObserversAboutChange)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            CheckForAndThrowIfDisposed();

            var wasAdded = InnerDictionary.TryAdd(key, value);
            if (wasAdded)
            {
                AddKeyToPropertyChangedHandling(key);
                AddValueToPropertyChangedHandling(value);

                if (notifyObserversAboutChange)
                    NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.ItemAdded(this, key, value));
            }

            return wasAdded;
        }

        /// <summary>
        /// Attempts to add the specified keys and values to the <see cref="ObservableDictionary{TKey,TValue}" />.
        /// </summary>
        /// <param name="items">The items to add.</param>
        /// <param name="itemsThatCouldNotBeAdded">The items that could not be added, typically due to pre-existing keys in the dictionary.</param>
        /// <returns>
        /// [true] if all key/value pairs were added to the <see cref="ObservableDictionary{TKey,TValue}" /> successfully; [false] if not.
        /// If [false] is returned, check <paramref name="itemsThatCouldNotBeAdded"/> for the corresponding ones that could not be added.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        ///   <paramref name="items" /> is  null.</exception>
        /// <exception cref="T:System.OverflowException">The dictionary already contains the maximum number of elements (<see cref="F:System.Int32.MaxValue" />).</exception>
        public virtual bool TryAddRange(IEnumerable<KeyValuePair<TKey, TValue>> items, out IDictionary<TKey, TValue> itemsThatCouldNotBeAdded)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            CheckForAndThrowIfDisposed();

            itemsThatCouldNotBeAdded = new Dictionary<TKey, TValue>();

            var itemsAsList = items.ToList();
            IDictionary<TKey, TValue> itemsThatCouldBeAdded = new Dictionary<TKey, TValue>();

            if (itemsAsList.Count == 0)
                return true;

            var useResetInsteadOfIndividualChanges = IsItemsChangedAmountGreaterThanResetThreshold(itemsAsList.Count, ThresholdAmountWhenChangesAreNotifiedAsReset);
            using ((useResetInsteadOfIndividualChanges && IsTrackingChanges) ? SuppressChangeNotifications(false) : Disposable.Empty)
            {
                // then perform change itself
                foreach (var keyValuePair in itemsAsList)
                {
                    // ... and only notify observers if 'currently' desired
                    if (TryAdd(keyValuePair.Key, keyValuePair.Value, !useResetInsteadOfIndividualChanges) == false)
                    {
                        itemsThatCouldNotBeAdded.Add(keyValuePair);
                    }
                    else
                    {
                        itemsThatCouldBeAdded.Add(keyValuePair);
                    }
                }
            }
            
            // if NO item at all could be added, return early (as there's nothing to notify observers about
            if (itemsThatCouldBeAdded.Count == 0)
                return false;

            // finally and if applicable (and currently wanted), signal a reset OR individual change(s)
            if (useResetInsteadOfIndividualChanges)
            {
                NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.Reset(this));
            }

            return itemsThatCouldNotBeAdded.Count == 0; // return true if non-addable items were / is 0
        }

        /// <summary>
        /// Attempts to remove and return the value that has the specified key from the <see cref="ObservableDictionary{TKey,TValue}"/>.
        /// </summary>
        /// <returns>
        /// true if the object was removed successfully; otherwise, false.
        /// </returns>
        /// <param name="key">The key of the element to remove and return.</param>
        /// <param name="value">This contains the object removed from the <see cref="ObservableDictionary{TKey,TValue}"/>, or the default value of the TValue type if <paramref name="key"/> does not exist.</param>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="key"/> is null.</exception>
        public virtual bool TryRemove(TKey key, out TValue value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            CheckForAndThrowIfDisposed();

            return TryRemove(key, out value, IsTrackingChanges);
        }

        /// <summary>
        /// Attempts to remove and return the value that has the specified key from the <see cref="ObservableDictionary{TKey,TValue}" />.
        /// </summary>
        /// <param name="key">The key of the element to remove and return.</param>
        /// <param name="value">This contains the object removed from the <see cref="ObservableDictionary{TKey,TValue}" />, or the default value of the TValue type if <paramref name="key" /> does not exist.</param>
        /// <param name="notifyObserversAboutChange">if set to <c>true</c> [notify observers about change].</param>
        /// <returns>
        /// true if the object was removed successfully; otherwise, false.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        ///   <paramref name="key" /> is null.</exception>
        protected virtual bool TryRemove(TKey key, out TValue value, bool notifyObserversAboutChange)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            CheckForAndThrowIfDisposed();

            var wasRemoved = InnerDictionary.TryRemove(key, out value);
            if (wasRemoved)
            {
                RemoveKeyFromPropertyChangedHandling(key);
                RemoveValueFromPropertyChangedHandling(value);

                if (notifyObserversAboutChange)
                {
                    NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.ItemRemoved(this, key, value));
                }
            }

            return wasRemoved;
        }

        /// <summary>
        /// Attempts to remove the <paramref name="items" /> from the <see cref="ObservableDictionary{TKey,TValue}" />.
        /// </summary>
        /// <param name="items">The key/value pairs to remove.</param>
        /// <param name="itemsThatCouldNotBeRemoved">The key/value pairs that could not be removed.</param>
        /// <returns>
        /// [true] if all key/value pairs were removed from the <see cref="ObservableDictionary{TKey,TValue}" /> successfully; [false] if not.
        /// If [false] is returned, check <paramref name="itemsThatCouldNotBeRemoved" /> for the corresponding ones that could not be removed.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        ///   <paramref name="items" /> is null.</exception>
        public virtual bool TryRemoveRange(IEnumerable<KeyValuePair<TKey, TValue>> items, out IDictionary<TKey, TValue> itemsThatCouldNotBeRemoved)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            CheckForAndThrowIfDisposed();

            itemsThatCouldNotBeRemoved = new Dictionary<TKey, TValue>();

            var keyValuePairs = items.ToList();
            IDictionary<TKey, TValue> keyValuePairsThatCouldBeRemoved = new Dictionary<TKey, TValue>();

            if (keyValuePairs.Count == 0)
                return true;

            // check whether change(s) shall be notified as individual changes OR as one final reset at the end
            var useResetInsteadOfIndividualChanges = IsItemsChangedAmountGreaterThanResetThreshold(keyValuePairs.Count, ThresholdAmountWhenChangesAreNotifiedAsReset);
            var signalIndividualItemChanges = !useResetInsteadOfIndividualChanges;

            // then perform removal itself
            foreach (var keyValuePair in keyValuePairs)
            {
                TValue existingValue = default(TValue);
                if (TryGetValue(keyValuePair.Key, out existingValue) == false
                    || AreTheSameValue(existingValue, keyValuePair.Value) == false)
                {
                    itemsThatCouldNotBeRemoved.Add(keyValuePair);
                    continue;
                }

                // else
                // perform basically a tryremove / double check again because since the first check something might have changed
                TValue value = default(TValue);
                if (TryRemove(keyValuePair.Key, out value, signalIndividualItemChanges && IsTrackingChanges) == false)
                {
                    // this is basically a double check
                    itemsThatCouldNotBeRemoved.Add(keyValuePair);
                }
                else
                {
                    keyValuePairsThatCouldBeRemoved.Add(keyValuePair);
                }
            }

            // check whether we could remove any items at all, if not return right away (as there's nothing to notify observers about
            if (keyValuePairsThatCouldBeRemoved.Count == 0)
                return false;

            // finally and if still correct, signal a reset OR individual change(s)
            useResetInsteadOfIndividualChanges = IsItemsChangedAmountGreaterThanResetThreshold(keyValuePairsThatCouldBeRemoved.Count, ThresholdAmountWhenChangesAreNotifiedAsReset);
            if (useResetInsteadOfIndividualChanges)
            {
                NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.Reset(this));
            }
            else
            {
                foreach (var keyValuePair in keyValuePairsThatCouldBeRemoved)
                {
                    NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.ItemRemoved(this, keyValuePair.Key, keyValuePair.Value));
                }
            }

            return itemsThatCouldNotBeRemoved.Count == 0;
        }

        /// <summary>
        /// Attempts to remove the items for the provided <paramref name="keys" /> from the <see cref="ObservableDictionary{TKey,TValue}" />.
        /// </summary>
        /// <param name="keys">The key(s) for the items to remove.</param>
        /// <param name="keysThatCouldNotBeRemoved">The keys that could not be removed.</param>
        /// <returns>
        /// [true] if all items for the provided keys were removed from the <see cref="ObservableDictionary{TKey,TValue}" /> successfully; [false] if not.
        /// If [false] is returned, check <paramref name="keysThatCouldNotBeRemoved" /> for the corresponding ones that could not be removed.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        ///   <paramref name="keys" /> is null.</exception>
        public virtual bool TryRemoveRange(IEnumerable<TKey> keys, out IList<TKey> keysThatCouldNotBeRemoved)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));

            CheckForAndThrowIfDisposed();

            keysThatCouldNotBeRemoved = new List<TKey>();

            var keysAsList = keys.ToList();
            var keysThatCouldBeRemoved = new List<KeyValuePair<TKey, TValue>>();

            if (keysAsList.Count == 0)
                return true;

            // check whether change(s) shall be notified as individual changes OR as one final reset at the end
            var useResetInsteadOfIndividualChanges = IsItemsChangedAmountGreaterThanResetThreshold(keysAsList.Count, ThresholdAmountWhenChangesAreNotifiedAsReset);
            var signalIndividualItemChanges = !useResetInsteadOfIndividualChanges;

            // then perform removal itself
            foreach (var key in keysAsList)
            {
                // perform a per-item tryremove
                TValue value = default(TValue);
                if (TryRemove(key, out value, signalIndividualItemChanges && IsTrackingChanges) == false)
                {
                    keysThatCouldNotBeRemoved.Add(key);
                }
                else
                {
                    keysThatCouldBeRemoved.Add(new KeyValuePair<TKey, TValue>(key, value));
                }
            }

            // check whether we could remove any items at all, if not return right away (as there's nothing to notify observers about
            if (keysThatCouldBeRemoved.Count == 0)
                return false;

            // finally and if still correct, signal a reset OR individual change(s)
            useResetInsteadOfIndividualChanges = IsItemsChangedAmountGreaterThanResetThreshold(keysThatCouldBeRemoved.Count, ThresholdAmountWhenChangesAreNotifiedAsReset);
            if (useResetInsteadOfIndividualChanges)
            {
                NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.Reset(this));
            }
            else
            {
                foreach (var keyValuePair in keysThatCouldBeRemoved)
                {
                    NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.ItemRemoved(this, keyValuePair.Key, keyValuePair.Value));
                }
            }

            return keysThatCouldNotBeRemoved.Count == 0;
        }

        /// <summary>
        /// Attempts to remove the <paramref name="key"/> and corresponding value from the <see cref="ObservableDictionary{TKey,TValue}"/>.
        /// </summary>
        /// <returns>
        /// true if the object was removed successfully; otherwise, false.
        /// </returns>
        /// <param name="key">The key of the element to remove.</param>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="key"/> is null.</exception>
        public virtual bool TryRemove(TKey key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            CheckForAndThrowIfDisposed();

            TValue removedValue;
            return TryRemove(key, out removedValue);
        }

        /// <summary>
        /// Attempts to update the value that has the specified key from the <see cref="ObservableDictionary{TKey,TValue}" />.
        /// </summary>
        /// <param name="key">The key of the element to update.</param>
        /// <param name="newValue">The new value.</param>
        /// <returns>
        /// true if the object was updated successfully; otherwise, false.
        /// </returns>
        /// <exception cref="System.ArgumentNullException"></exception>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="key" /> is null.</exception>
        public virtual bool TryUpdate(TKey key, TValue newValue)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            CheckForAndThrowIfDisposed();

            if (!ContainsKey(key))
                return false;

            // else
            try
            {
                this[key] = newValue;
                return true;
            }
            catch (Exception)
            {
                // this is  basically a double check - ContainsKey only let through if the value did exist at the time of checking,
                // however in rare / race conditions someone might have deleted it in the 'meantime'
                return false;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Determines whether <paramref name="value1"/> and <paramref name="value2"/> are the same value.
        /// If <typeparamref name="TValue"/> is a value type, <see cref="object.Equals(object,object)"/> will be used,
        /// for reference types <see cref="object.ReferenceEquals"/> will be used.
        /// </summary>
        /// <param name="value1">The first value.</param>
        /// <param name="value2">The second value.</param>
        /// <returns></returns>
        protected virtual bool AreTheSameValue(TValue value1, TValue value2)
        {
            return ValueComparer.Equals(value1, value2);
        }

        /// <summary>
        /// Handles <see cref="INotifyPropertyChanged.PropertyChanged"/> events for <typeparamref name="TKey"/> instances
        /// - if that type implements <see cref="INotifyPropertyChanged"/>.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="PropertyChangedEventArgs"/> instance containing the event data.</param>
        protected virtual void OnKeyPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (IsDisposing)
                return;

            CheckForAndThrowIfDisposed(false);

            if (!IsTrackingItemChanges)
                return;

            // inspiration taken from BindingList, see http://referencesource.microsoft.com/#System/compmod/system/componentmodel/BindingList.cs,560
            if (sender == null || e == null || string.IsNullOrWhiteSpace(e.PropertyName))
            {
                // Fire reset event (per INotifyPropertyChanged spec)
                NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.Reset(this));
            }
            else
            {
                // The change event is broken should someone pass an item to us that is not
                // of type TKey. Still, if they do so, detect it and ignore.  It is an incorrect
                // and rare enough occurrence that we do not want to slow the mainline path
                // with "is" checks.
                TKey key;

                try
                {
                    key = (TKey)sender;
                }
                catch (InvalidCastException)
                {
                    NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.Reset(this));
                    return;
                }

                // otherwise check whether the amount of notifications would be greater than the individual messages threshold
                if (IsItemsChangedAmountGreaterThanResetThreshold(1, ThresholdAmountWhenChangesAreNotifiedAsReset))
                {
                    NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.Reset(this));
                }
                else
                {
                    // if not, send out individual item changed notification
                    NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.ItemKeyChanged(this, key, e.PropertyName));
                }
            }
        }

        /// <summary>
        /// Handles <see cref="INotifyPropertyChanged.PropertyChanged"/> events for <typeparamref name="TValue"/> instances
        /// - if that type implements <see cref="INotifyPropertyChanged"/>.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="PropertyChangedEventArgs"/> instance containing the event data.</param>
        protected virtual void OnValuePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (IsDisposing)
                return;

            CheckForAndThrowIfDisposed(false);

            if (!IsTrackingItemChanges)
                return;

            // inspiration taken from BindingList, see http://referencesource.microsoft.com/#System/compmod/system/componentmodel/BindingList.cs,560
            if (sender == null || e == null || string.IsNullOrWhiteSpace(e.PropertyName))
            {
                // Fire reset event (per INotifyPropertyChanged spec)
                NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.Reset(this));
            }
            else
            {
                // The change event is broken should someone pass an item to us that is not
                // of type TValue. Still, if they do so, detect it and ignore.  It is an incorrect
                // and rare enough occurrence that we do not want to slow the mainline path
                // with "is" checks.
                TValue item;

                try
                {
                    item = (TValue)sender;
                }
                catch (InvalidCastException)
                {
                    NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.Reset(this));
                    return;
                }
                
                // otherwise check whether the amount of notifications would be greater than the individual messages threshold
                if (IsItemsChangedAmountGreaterThanResetThreshold(1, ThresholdAmountWhenChangesAreNotifiedAsReset))
                {
                    NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.Reset(this));
                }
                else
                {
                    // if not, send out individual item changed notification
                    NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.ItemValueChanged(this, item, e.PropertyName));
                }
            }
        }

        /// <summary>
        /// Adds <see cref="OnKeyPropertyChanged"/> as event handler for <paramref name="key"/>'s <see cref="INotifyPropertyChanged.PropertyChanged"/> event.
        /// </summary>
        /// <param name="key">The key.</param>
        private void AddKeyToPropertyChangedHandling(TKey key)
        {
            CheckForAndThrowIfDisposed();

            var keyAsINotifyPropertyChanged = (key as INotifyPropertyChanged);
            if (keyAsINotifyPropertyChanged != null)
            {
                keyAsINotifyPropertyChanged.PropertyChanged += OnKeyPropertyChanged;
            }
        }

        /// <summary>
        /// Adds <see cref="OnValuePropertyChanged"/> as event handler for <paramref name="value"/>'s <see cref="INotifyPropertyChanged.PropertyChanged"/> event.
        /// </summary>
        /// <param name="value">The value.</param>
        private void AddValueToPropertyChangedHandling(TValue value)
        {
            CheckForAndThrowIfDisposed();

            var valueAsINotifyPropertyChanged = (value as INotifyPropertyChanged);
            if (valueAsINotifyPropertyChanged != null)
            {
                valueAsINotifyPropertyChanged.PropertyChanged += OnValuePropertyChanged;
            }
        }

        /// <summary>
        /// Removes <see cref="OnValuePropertyChanged"/> as event handler for <paramref name="value"/>'s <see cref="INotifyPropertyChanged.PropertyChanged"/> event.
        /// </summary>
        /// <param name="value">The value.</param>
        private void RemoveValueFromPropertyChangedHandling(TValue value)
        {
            CheckForAndThrowIfDisposed();

            var valueAsINotifyPropertyChanged = (value as INotifyPropertyChanged);
            if (valueAsINotifyPropertyChanged != null)
            {
                valueAsINotifyPropertyChanged.PropertyChanged -= OnValuePropertyChanged;
            }
        }

        /// <summary>
        /// Removes <see cref="OnKeyPropertyChanged"/> as event handler for <paramref name="key"/>'s <see cref="INotifyPropertyChanged.PropertyChanged"/> event.
        /// </summary>
        /// <param name="key">The value.</param>
        private void RemoveKeyFromPropertyChangedHandling(TKey key)
        {
            CheckForAndThrowIfDisposed();

            var keyAsINotifyPropertyChanged = (key as INotifyPropertyChanged);
            if (keyAsINotifyPropertyChanged != null)
            {
                keyAsINotifyPropertyChanged.PropertyChanged -= OnKeyPropertyChanged;
            }
        }

        /// <summary>
        /// Notifies subscribers about the given <paramref name="observableDictionaryChange"/>.
        /// </summary>
        /// <param name="observableDictionaryChange">The observable dictionary change.</param>
        /// <exception cref="System.ArgumentNullException"></exception>
        protected virtual void NotifyObserversAboutDictionaryChanges(IObservableDictionaryChange<TKey, TValue> observableDictionaryChange)
        {
            if (observableDictionaryChange == null) throw new ArgumentNullException(nameof(observableDictionaryChange));

            CheckForAndThrowIfDisposed();

            // go ahead and check whether a Reset or item add, -change, -move or -remove shall be signaled
            // .. based on the ThresholdAmountWhenChangesAreNotifiedAsReset value
            var actualObservableDictionaryChange =
                (observableDictionaryChange.ChangeType == ObservableDictionaryChangeType.Reset
                 || IsItemsChangedAmountGreaterThanResetThreshold(1, ThresholdAmountWhenChangesAreNotifiedAsReset))
                    ? ObservableDictionaryChange<TKey, TValue>.Reset(this)
                    : observableDictionaryChange;

            if (actualObservableDictionaryChange.ChangeType == ObservableDictionaryChangeType.ItemAdded
                || actualObservableDictionaryChange.ChangeType == ObservableDictionaryChangeType.ItemRemoved
                || actualObservableDictionaryChange.ChangeType == ObservableDictionaryChangeType.Reset)
            {
                try
                {
                    if(IsTrackingCountChanges)
                        CountChangesObserver.OnNext(Count);
                }
                catch (Exception exception)
                {
                    var observerException = new ObserverException(
                        $"An error occured notifying {nameof(CountChanges)} Observers of this {this.GetType().Name}.",
                        exception);

                    ObserverExceptionsObserver.OnNext(observerException);

                    if (observerException.Handled == false)
                        throw;
                }
            }

            try
            {
                DictionaryChangesObserver.OnNext(actualObservableDictionaryChange);
            }
            catch (Exception exception)
            {
                var observerException = new ObserverException(
                    $"An error occured notifying {nameof(DictionaryChanges)} Observers of this {this.GetType().Name}.",
                    exception);

                ObserverExceptionsObserver.OnNext(observerException);

                if (observerException.Handled == false)
                    throw;
            }
            
            try
            {
                RaiseCollectionChanged(actualObservableDictionaryChange);
            }
            catch (Exception exception)
            {
                var observerException = new ObserverException(
                    $"An error occured notifying CollectionChanged Subscribers of this {this.GetType().Name}.",
                    exception);

                ObserverExceptionsObserver.OnNext(observerException);

                if (observerException.Handled == false)
                    throw;
            }
        }

        /// <summary>
        ///     Determines whether the amount of changed items is greater than the reset threshold and / or the minimum amount of
        ///     items to be considered as a reset.
        /// </summary>
        /// <param name="affectedItemsCount">The items changed / affected.</param>
        /// <param name="maximumAmountOfItemsChangedToBeConsideredResetThreshold">
        ///     The maximum amount of changed items count to
        ///     consider a change or a range of changes a reset.
        /// </param>
        /// <returns></returns>
        protected virtual bool IsItemsChangedAmountGreaterThanResetThreshold(int affectedItemsCount, int maximumAmountOfItemsChangedToBeConsideredResetThreshold)
        {
            if (affectedItemsCount <= 0) throw new ArgumentOutOfRangeException(nameof(affectedItemsCount));
            if (maximumAmountOfItemsChangedToBeConsideredResetThreshold < 0) throw new ArgumentOutOfRangeException(nameof(maximumAmountOfItemsChangedToBeConsideredResetThreshold));

            // check for '0' thresholds
            if (maximumAmountOfItemsChangedToBeConsideredResetThreshold == 0)
                return true;

            return affectedItemsCount >= maximumAmountOfItemsChangedToBeConsideredResetThreshold;
        }

        /// <summary>
        /// Prepares and sets up the observables and subjects used, particularly
        /// <see cref="_dictionaryChangesSubject"/>, <see cref="_countChangesSubject"/> and <see cref="_unhandledObserverExceptionsSubject"/> and also notifications for
        /// 'Count' and 'Items[]' <see cref="INotifyPropertyChanged"/> events on <see cref="CountChanges"/> and <see cref="DictionaryChanges"/>
        /// occurrences (for WPF / Binding)
        /// </summary>
        private void SetupObservablesAndObserversAndSubjects()
        {
            // prepare subjects for RX
            ObserverExceptionsObserver = _unhandledObserverExceptionsSubject.NotifyOn(Scheduler);
            DictionaryChangesObserver = _dictionaryChangesSubject.NotifyOn(Scheduler);
            CountChangesObserver = _countChangesSubject.NotifyOn(Scheduler);

            //// 'Count' and 'Item[]' PropertyChanged events are used by WPF typically via / for ObservableCollections, see
            //// http://referencesource.microsoft.com/#System/compmod/system/collections/objectmodel/observablecollection.cs,421
            _countChangesCountPropertyChangedForwarder = CountChanges
                .ObserveOn(Scheduler)
                .Subscribe(_ => RaisePropertyChanged(nameof(Count)));

            _dictionaryChangesItemIndexerPropertyChangedForwarder = DictionaryChanges
                .ObserveOn(Scheduler)
                .Subscribe(_ => RaisePropertyChanged(ItemIndexerName));
        }

        #endregion

        #region Implementation of IDisposable

        private long _isDisposing = 0;
        private long _isDisposed = 0;

        private readonly object _isDisposedLocker = new object();

        /// <summary>
        ///     Gets or sets a value indicating whether this instance has been disposed.
        /// </summary>
        /// <value>
        ///     <c>true</c> if this instance is disposed; otherwise, <c>false</c>.
        /// </value>
        public virtual bool IsDisposed
        {
            get { return Interlocked.Read(ref _isDisposed) == 1; }
            protected set
            {
                lock (_isDisposedLocker)
                {
                    if (value == false && IsDisposed)
                        throw new InvalidOperationException("Once Disposed has been set, it cannot be reset back to false.");

                    Interlocked.Exchange(ref _isDisposed, value ? 1 : 0);
                }
            }
        }

        /// <summary>
        ///     Gets or sets a value indicating whether this instance is disposing.
        /// </summary>
        /// <value>
        ///     <c>true</c> if this instance is disposing; otherwise, <c>false</c>.
        /// </value>
        public virtual bool IsDisposing
        {
            get { return Interlocked.Read(ref _isDisposing) == 1; }
            protected set
            {
                Interlocked.Exchange(ref _isDisposing, value ? 1 : 0);
            }
        }

        /// <summary>
        ///     Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public virtual void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///     Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposeManagedResources">
        ///     <c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.
        /// </param>
        protected virtual void Dispose(bool disposeManagedResources)
        {
            if (IsDisposing || IsDisposed)
                return;

            try
            {
                IsDisposing = true;

                if (disposeManagedResources)
                {
                    // clear inner dictionary early on
                    IDisposable innerDictionaryClearingNotificationSuppression = !IsTrackingChanges
                        ? Disposable.Empty
                        : SuppressChangeNotifications(false);
                    InnerDictionary.Clear();
                    innerDictionaryClearingNotificationSuppression?.Dispose();
                    
                    if (_dictionaryChangesItemIndexerPropertyChangedForwarder != null)
                    {
                        _dictionaryChangesItemIndexerPropertyChangedForwarder.Dispose();
                        _dictionaryChangesItemIndexerPropertyChangedForwarder = null;
                    }

                    if (_countChangesCountPropertyChangedForwarder != null)
                    {
                        _countChangesCountPropertyChangedForwarder.Dispose();
                        _countChangesCountPropertyChangedForwarder = null;
                    }

                    var countChangesObserverAsDisposable = CountChangesObserver as IDisposable;
                    countChangesObserverAsDisposable?.Dispose();
                    CountChangesObserver = null;

                    if (_countChangesSubject != null)
                    {
                        _countChangesSubject.Dispose();
                        _countChangesSubject = null;
                    }

                    var dictionaryChangesObserverAsDisposable = DictionaryChangesObserver as IDisposable;
                    dictionaryChangesObserverAsDisposable?.Dispose();
                    DictionaryChangesObserver = null;

                    if (_dictionaryChangesSubject != null)
                    {
                        _dictionaryChangesSubject.Dispose();
                        _dictionaryChangesSubject = null;
                    }

                    var thrownExceptionsObserverAsDisposable = ObserverExceptionsObserver as IDisposable;
                    thrownExceptionsObserverAsDisposable?.Dispose();
                    ObserverExceptionsObserver = null;

                    if (_unhandledObserverExceptionsSubject != null)
                    {
                        _unhandledObserverExceptionsSubject.Dispose();
                        _unhandledObserverExceptionsSubject = null;
                    }
                }
            }
            finally
            {
                IsDisposing = false;
                IsDisposed = true;
            }
        }

        /// <summary>
        /// Checks whether this instance has been disposed, optionally whether it is currently being disposed.
        /// </summary>
        /// <param name="checkIsDisposing">if set to <c>true</c> checks whether disposal is currently ongoing, indicated via <see cref="IsDisposing"/>.</param>
        protected virtual void CheckForAndThrowIfDisposed(bool checkIsDisposing = true)
        {
            if (checkIsDisposing && IsDisposing)
            {
                throw new ObjectDisposedException(GetType().Name, "This instance is currently being disposed.");
            }

            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        #endregion

        #region Implementation of INotifyPropertyChanged

        /// <summary>
        ///     The actual <see cref="PropertyChanged" /> event.
        /// </summary>
        private PropertyChangedEventHandler _propertyChanged;

        /// <summary>
        ///     Occurs when a property changed.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged
        {
            add
            {
                CheckForAndThrowIfDisposed();
                _propertyChanged += value;
            }
            remove
            {
                CheckForAndThrowIfDisposed();
                _propertyChanged -= value;
            }
        }

        /// <summary>
        ///     Raises the property changed event.
        /// </summary>
        /// <param name="propertyName">Name of the property.</param>
        protected virtual void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            if (IsDisposed || IsDisposing)
                return;

            var eventHandler = _propertyChanged;
            if (eventHandler != null)
            {
                Scheduler.Schedule(() => eventHandler.Invoke(this, new PropertyChangedEventArgs(propertyName)));
            }
        }

        #endregion

        #region Implementation of INotifyObserverExceptions

        /// <summary>
        /// Provides an observable sequence of <see cref="ObserverException">exceptions</see> thrown by observers.
        /// An <see cref="ObserverException" /> provides a <see cref="ObserverException.Handled" /> property, if set to [true] by
        /// any of the observers of <see cref="ObserverExceptions" /> observable, it is assumed to be safe to continue
        /// without re-throwing the exception.
        /// </summary>
        /// <value>
        /// An observable stream of unhandled exceptions.
        /// </value>
        public virtual IObservable<ObserverException> ObserverExceptions
        {
            get
            {
                CheckForAndThrowIfDisposed();

                // not caring about IsDisposing / IsDisposed on purpose once subscribed, so corresponding Exceptions are forwarded 'til the "end" to already existing subscribers
                return _unhandledObserverExceptionsSubject;
            }
        }

        #endregion

        #region Implementation of INotifyObservableResets

        /// <summary>
        /// Gets a value indicating whether this instance is tracking and notifying about
        /// list / collection resets, typically for data binding.
        /// </summary>
        /// <value>
        /// <c>true</c> if this instance is tracking resets; otherwise, <c>false</c>.
        /// </value>

        private readonly object _isTrackingResetsLocker = new object();
        private long _isTrackingResets = 0;

        /// <summary>
        ///     Gets a value indicating whether this instance is tracking resets.
        /// </summary>
        /// <value>
        ///     <c>true</c> if this instance is tracking resets; otherwise, <c>false</c>.
        /// </value>
        public bool IsTrackingResets
        {
            get
            {
                CheckForAndThrowIfDisposed(false);

                return Interlocked.Read(ref _isTrackingResets) == 1;
            }
            protected set
            {
                CheckForAndThrowIfDisposed(false);

                lock (_isTrackingResetsLocker)
                {
                    if (value == false && IsTrackingResets == false)
                        throw new InvalidOperationException("A Reset(s) Notification Suppression is currently already ongoing, multiple concurrent suppressions are not supported.");

                    // First set marker here to prevent re-entry
                    Interlocked.Exchange(ref _isTrackingResets, value ? 1 : 0);

                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets the reset notifications as an observable stream.  Whenever signaled,
        /// observers should reset any knowledge / state etc about the list.
        /// </summary>
        /// <value>
        /// The resets.
        /// </value>
        public virtual IObservable<Unit> Resets
        {
            get
            {
                CheckForAndThrowIfDisposed();

                return DictionaryChanges
                    .TakeWhile(_ => !IsDisposing && !IsDisposed)
                    .Where(change => change.ChangeType == ObservableDictionaryChangeType.Reset)
                    .SkipContinuouslyWhile(_ => !IsTrackingResets)
                    .Select(_ => Unit.Default);
            }
        }

        /// <summary>
        /// (Temporarily) suppresses change notifications for resets until the <see cref="IDisposable" /> handed over to the caller
        /// has been Disposed and then a Reset will be signaled, if wanted and applicable.
        /// </summary>
        /// <param name="signalResetWhenFinished">if set to <c>true</c> signals a reset when finished.</param>
        /// <returns></returns>
        public virtual IDisposable SuppressResetNotifications(bool signalResetWhenFinished = true)
        {
            CheckForAndThrowIfDisposed(false);

            IsTrackingResets = false;

            return Disposable.Create(() =>
            {
                IsTrackingResets = true;

                if (signalResetWhenFinished)
                {
                    NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.Reset(this));
                }
            });
        }

        #endregion

        #region Implementation of IObservableCollection<T>

        /// <summary>
		/// Signals subscribers that they should reset their and local state about this instance by
		/// signaling a <see cref="ObservableCollectionChangeType.Reset"/> message and event.
		/// </summary>
        public virtual void Reset()
        {
            CheckForAndThrowIfDisposed();

            if (IsTrackingResets == false)
                return;

            // else
            NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.Reset(this));
        }

        #endregion

        #region Implementation of INotifyObservableCountChanges

        private readonly object _isTrackingCountChangesLocker = new object();
        private long _isTrackingCountChanges = 0;

        /// <summary>
        ///     Gets a value indicating whether this instance is tracking <see cref="IReadOnlyCollection{T}.Count" /> changes.
        /// </summary>
        /// <value>
        ///     <c>true</c> if this instance is tracking resets; otherwise, <c>false</c>.
        /// </value>
        public bool IsTrackingCountChanges
        {
            get
            {
                CheckForAndThrowIfDisposed(false);

                return Interlocked.Read(ref _isTrackingCountChanges) == 1;
            }
            protected set
            {
                CheckForAndThrowIfDisposed(false);

                lock (_isTrackingCountChangesLocker)
                {
                    if (value == false && IsTrackingCountChanges == false)
                        throw new InvalidOperationException("A Count Change(s) Notification Suppression is currently already ongoing, multiple concurrent suppressions are not supported.");

                    // First set marker here to prevent re-entry
                    Interlocked.Exchange(ref _isTrackingCountChanges, value ? 1 : 0);

                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets the count change notifications as an observable stream.
        /// </summary>
        /// <value>
        /// The count changes.
        /// </value>
        public virtual IObservable<int> CountChanges
        {
            get
            {
                CheckForAndThrowIfDisposed();

                return _countChangesSubject
                    .TakeWhile(_ => !IsDisposing && !IsDisposed)
                    .SkipContinuouslyWhile(_ => !IsTrackingCountChanges)
                    .DistinctUntilChanged();
            }
        }

        /// <summary>
        /// (Temporarily) suppresses item count change notification until the returned <see cref="IDisposable" />
        /// has been Disposed.
        /// </summary>
        /// <param name="signalCurrentCountWhenFinished">if set to <c>true</c> signals a the current count when disposed.</param>
        /// <returns></returns>
        public virtual IDisposable SuppressCountChangeNotifications(bool signalCurrentCountWhenFinished = true)
        {
            CheckForAndThrowIfDisposed(false);

            IsTrackingCountChanges = false;

            return Disposable.Create(() =>
            {
                IsTrackingCountChanges = true;

                if (signalCurrentCountWhenFinished)
                {
                    CountChangesObserver.OnNext(Count);
                }
            });
        }

        #endregion

        #region Implementation of INotifyObservableItemChanged

        /// <summary>
        /// (Temporarily) suppresses change notifications for <see cref="ObservableCollectionChangeType.ItemChanged"/> events until the returned <see cref="IDisposable" />
        /// has been Disposed and a Reset will be signaled, if applicable.
        /// </summary>
        /// <param name="signalResetWhenFinished">if set to <c>true</c> signals a reset when finished.</param>
        /// <returns></returns>
        public virtual IDisposable SuppressItemChangeNotifications(bool signalResetWhenFinished = true)
        {
            CheckForAndThrowIfDisposed(false);

            IsTrackingItemChanges = false;

            return Disposable.Create(() =>
            {
                IsTrackingItemChanges = true;

                if (signalResetWhenFinished)
                {
                    NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.Reset(this));
                }
            });
        }

        private readonly object _isTrackingItemChangesLocker = new object();
        private long _isTrackingItemChanges = 0;

        /// <summary>
        /// Gets a value indicating whether this instance has per item change tracking enabled and therefore listens to
        /// <see cref="INotifyPropertyChanged.PropertyChanged" /> events, if that interface is implemented, too.
        /// </summary>
        /// <value>
        /// <c>true</c> if this instance has item change tracking enabled; otherwise, <c>false</c>.
        /// </value>
        /// <exception cref="System.InvalidOperationException">An Item Change Notification Suppression is currently already ongoing, multiple concurrent suppressions are not supported.</exception>
        public bool IsTrackingItemChanges
        {
            get
            {
                CheckForAndThrowIfDisposed(false);

                return Interlocked.Read(ref _isTrackingItemChanges) == 1;
            }
            protected set
            {
                CheckForAndThrowIfDisposed(false);

                lock (_isTrackingItemChangesLocker)
                {
                    if (value == false && IsTrackingItemChanges == false)
                        throw new InvalidOperationException("An Item Change Notification Suppression is currently already ongoing, multiple concurrent suppressions are not supported.");

                    // First set marker here to prevent re-entry
                    Interlocked.Exchange(ref _isTrackingItemChanges, value ? 1 : 0);

                    RaisePropertyChanged();
                }
            }
        }

        private volatile int _thresholdAmountWhenItemChangesAreNotifiedAsReset;

        /// <summary>
        /// Gets or sets the threshold of the minimum amount of changes to switch individual notifications to a reset one.
        /// </summary>
        /// <value>
        /// The minimum items changed to be considered as a reset.
        /// </value>
        public int ThresholdAmountWhenChangesAreNotifiedAsReset
        {
            get
            {
                CheckForAndThrowIfDisposed(false);

                return _thresholdAmountWhenItemChangesAreNotifiedAsReset;
            }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Must be 0 or higher.");

                CheckForAndThrowIfDisposed();

                if (Equals(_thresholdAmountWhenItemChangesAreNotifiedAsReset, value))
                    return;

                // else
                _thresholdAmountWhenItemChangesAreNotifiedAsReset = value;

                RaisePropertyChanged();
            }
        }

        #endregion

        #region Implementation of INotifyObservableDictionaryChanges<TKey,TValue>

        /// <summary>
        /// Gets the dictionary changes as an observable stream.
        /// </summary>
        /// <value>
        /// The dictionary changes.
        /// </value>
        public virtual IObservable<IObservableDictionaryChange<TKey, TValue>> DictionaryChanges
        {
            get
            {
                CheckForAndThrowIfDisposed();

                return _dictionaryChangesSubject
                    .TakeWhile(_ => !IsDisposing && !IsDisposed)
                    .SkipContinuouslyWhile(change => !IsTrackingChanges)
                    .SkipContinuouslyWhile(change => change.ChangeType == ObservableDictionaryChangeType.ItemKeyChanged && !IsTrackingItemChanges)
                    .SkipContinuouslyWhile(change => change.ChangeType == ObservableDictionaryChangeType.ItemValueChanged && !IsTrackingItemChanges)
                    .SkipContinuouslyWhile(change => change.ChangeType == ObservableDictionaryChangeType.ItemValueReplaced && !IsTrackingItemChanges)
                    .SkipContinuouslyWhile(change => change.ChangeType == ObservableDictionaryChangeType.Reset && !IsTrackingResets);
            }
        }
        
        #endregion

        #region Implementation of INotifyObservableChanges

        /// <summary>
        /// (Temporarily) suppresses change notifications until the returned <see cref="IDisposable" />
        /// has been Disposed and a Reset will be signaled, if wanted and applicable.
        /// </summary>
        /// <param name="signalResetWhenFinished">if set to <c>true</c> signals a reset when finished.</param>
        /// <returns></returns>
        public virtual IDisposable SuppressChangeNotifications(bool signalResetWhenFinished = true)
        {
            CheckForAndThrowIfDisposed(false);

            IsTrackingChanges = false;

            return Disposable.Create(() =>
            {
                IsTrackingChanges = true;

                if (signalResetWhenFinished)
                {
                    NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.Reset(this));
                }
            });
        }

        private readonly object _isTrackingChangesLocker = new object();
        private long _isTrackingChanges = 0;

        /// <summary>
        /// Gets a value indicating whether this instance is currently suppressing observable change notifications of any kind.
        /// </summary>
        /// <value>
        /// <c>true</c> if this instance is suppressing observable change notifications; otherwise, <c>false</c>.
        /// </value>
        /// <exception cref="System.InvalidOperationException">A Change Notification Suppression is currently already ongoing, multiple concurrent suppressions are not supported.</exception>
        public bool IsTrackingChanges
        {
            get
            {
                CheckForAndThrowIfDisposed(false);

                return Interlocked.Read(ref _isTrackingChanges) == 1;
            }
            protected set
            {
                CheckForAndThrowIfDisposed(false);

                lock (_isTrackingChangesLocker)
                {
                    if (value == false && IsTrackingChanges == false)
                        throw new InvalidOperationException("A Change Notification Suppression is currently already ongoing, multiple concurrent suppressions are not supported.");

                    // First set marker here to prevent re-entry
                    Interlocked.Exchange(ref _isTrackingChanges, value ? 1 : 0);

                    RaisePropertyChanged();
                }
            }
        }

        #endregion

        #region Implementation of ICollection

        /// <summary>
        /// Copies the elements of the <see cref="T:System.Collections.ICollection"/> to an <see cref="T:System.Array"/>, starting at a particular <see cref="T:System.Array"/> index.
        /// </summary>
        /// <param name="array">The one-dimensional <see cref="T:System.Array"/> that is the destination of the elements copied from <see cref="T:System.Collections.ICollection"/>. The <see cref="T:System.Array"/> must have zero-based indexing. </param><param name="index">The zero-based index in <paramref name="array"/> at which copying begins. </param><exception cref="T:System.ArgumentNullException"><paramref name="array"/> is null. </exception><exception cref="T:System.ArgumentOutOfRangeException"><paramref name="index"/> is less than zero. </exception><exception cref="T:System.ArgumentException"><paramref name="array"/> is multidimensional.-or- The number of elements in the source <see cref="T:System.Collections.ICollection"/> is greater than the available space from <paramref name="index"/> to the end of the destination <paramref name="array"/>.-or-The type of the source <see cref="T:System.Collections.ICollection"/> cannot be cast automatically to the type of the destination <paramref name="array"/>.</exception>
        void ICollection.CopyTo(Array array, int index)
        {
            CheckForAndThrowIfDisposed();

            ((ICollection)InnerDictionary).CopyTo(array, index);
        }

        /// <summary>
        /// Gets an object that can be used to synchronize access to the <see cref="T:System.Collections.ICollection"/>.
        /// </summary>
        /// <returns>
        /// An object that can be used to synchronize access to the <see cref="T:System.Collections.ICollection"/>.
        /// </returns>
        object ICollection.SyncRoot
        {
            get
            {
                CheckForAndThrowIfDisposed();

                return ((ICollection)InnerDictionary).SyncRoot;
            }
        }

        /// <summary>
        /// Gets a value indicating whether access to the <see cref="T:System.Collections.ICollection"/> is synchronized (thread safe).
        /// </summary>
        /// <returns>
        /// true if access to the <see cref="T:System.Collections.ICollection"/> is synchronized (thread safe); otherwise, false.
        /// </returns>
        bool ICollection.IsSynchronized
        {
            get
            {
                CheckForAndThrowIfDisposed();

                return ((ICollection)InnerDictionary).IsSynchronized;
            }
        }

        /// <summary>
        /// Gets the number of elements in the collection.
        /// </summary>
        /// <returns>
        /// The number of elements in the collection. 
        /// </returns>
        int ICollection.Count
        {
            get
            {
                CheckForAndThrowIfDisposed();

                return Count;
            }
        }

        /// <summary>
        /// Gets an <see cref="T:System.Collections.Generic.ICollection`1"/> containing the values in the <see cref="T:System.Collections.Generic.IDictionary`2"/>.
        /// </summary>
        /// <returns>
        /// An <see cref="T:System.Collections.Generic.ICollection`1"/> containing the values in the object that implements <see cref="T:System.Collections.Generic.IDictionary`2"/>.
        /// </returns>
        public virtual ICollection<TValue> Values
        {
            get
            {
                CheckForAndThrowIfDisposed();

                return InnerDictionary.Values;
            }
        }

        /// <summary>
        /// Gets an <see cref="T:System.Collections.Generic.ICollection`1"/> containing the keys of the <see cref="T:System.Collections.Generic.IDictionary`2"/>.
        /// </summary>
        /// <returns>
        /// An <see cref="T:System.Collections.Generic.ICollection`1"/> containing the keys of the object that implements <see cref="T:System.Collections.Generic.IDictionary`2"/>.
        /// </returns>
        public virtual ICollection<TKey> Keys
        {
            get
            {
                CheckForAndThrowIfDisposed();

                return InnerDictionary.Keys;
            }
        }

        #endregion

        #region Implementation of IReadOnlyCollection<out KeyValuePair<TKey,TValue>>

        /// <summary>
        /// Gets the number of elements in the collection.
        /// </summary>
        /// <returns>
        /// The number of elements in the collection. 
        /// </returns>
        public virtual int Count
        {
            get
            {
                CheckForAndThrowIfDisposed();

                return InnerDictionary.Count;
            }
        }

        #endregion

        #region Implementation of IEnumerable<out KeyValuePair<TKey,TValue>>

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>
        /// An enumerator that can be used to iterate through the collection.
        /// </returns>
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
        {
            CheckForAndThrowIfDisposed();

            return InnerDictionary.GetEnumerator();
        }

        #endregion

        #region Implementation of INotifyCollectionChanged


        /// <summary>
        ///     The actual <see cref="INotifyCollectionChanged.CollectionChanged" /> event.
        /// </summary>
        private NotifyCollectionChangedEventHandler _collectionChanged;

        /// <summary>
        ///     Occurs when the collection changed.
        /// </summary>
        event NotifyCollectionChangedEventHandler INotifyCollectionChanged.CollectionChanged
        {
            add
            {
                CheckForAndThrowIfDisposed();
                _collectionChanged += value;
            }
            remove
            {
                CheckForAndThrowIfDisposed();
                _collectionChanged -= value;
            }
        }

        /// <summary>
        /// Raises the <see cref="E:CollectionChanged" /> event.
        /// </summary>
        /// <param name="observableDictionaryChange">The observable dictionary change.</param>
        protected virtual void RaiseCollectionChanged(IObservableDictionaryChange<TKey, TValue> observableDictionaryChange)
        {
            if (observableDictionaryChange == null)
                throw new ArgumentNullException(nameof(observableDictionaryChange));

            if (IsDisposed || IsDisposing)
                return;

            // only raise event if it's currently allowed
            if (!IsTrackingChanges
                || (observableDictionaryChange.ChangeType == ObservableDictionaryChangeType.ItemKeyChanged && !IsTrackingItemChanges)
                || (observableDictionaryChange.ChangeType == ObservableDictionaryChangeType.ItemValueChanged && !IsTrackingItemChanges)
                || (observableDictionaryChange.ChangeType == ObservableDictionaryChangeType.ItemValueReplaced && !IsTrackingItemChanges)
                || (observableDictionaryChange.ChangeType == ObservableDictionaryChangeType.Reset && !IsTrackingResets))
            {
                return;
            }

            var eventHandler = _collectionChanged;
            if (eventHandler != null)
            {
                Scheduler.Schedule(() =>
                {
                    foreach (var notifyCollectionChangedEventArgs in observableDictionaryChange.ToObservableCollectionChanges(this, ValueComparer).Select(collectionChange => collectionChange.ToNotifyCollectionChangedEventArgs()))
                    {
                        eventHandler?.Invoke(this, notifyCollectionChangedEventArgs);
                    }
                });
            }
        }

        #endregion

        #region Implementation of IEnumerable

        /// <summary>
        /// Returns an enumerator that iterates through a collection.
        /// </summary>
        /// <returns>
        /// An <see cref="T:System.Collections.IEnumerator"/> object that can be used to iterate through the collection.
        /// </returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            CheckForAndThrowIfDisposed();

            return ((IEnumerable<KeyValuePair<TKey, TValue>>)this).GetEnumerator();
        }

        #endregion

        #region Implementation of IReadOnlyDictionary<TKey,TValue>

        /// <summary>
        /// Determines whether the read-only dictionary contains an element that has the specified key.
        /// </summary>
        /// <returns>
        /// true if the read-only dictionary contains an element that has the specified key; otherwise, false.
        /// </returns>
        /// <param name="key">The key to locate.</param><exception cref="T:System.ArgumentNullException"><paramref name="key"/> is null.</exception>
        public virtual bool ContainsKey(TKey key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            CheckForAndThrowIfDisposed();

            return InnerDictionary.ContainsKey(key);
        }

        /// <summary>
        /// Adds an element with the provided key and value to the <see cref="T:System.Collections.Generic.IDictionary`2"/>.
        /// </summary>
        /// <param name="key">The object to use as the key of the element to add.</param><param name="value">The object to use as the value of the element to add.</param><exception cref="T:System.ArgumentNullException"><paramref name="key"/> is null.</exception><exception cref="T:System.ArgumentException">An element with the same key already exists in the <see cref="T:System.Collections.Generic.IDictionary`2"/>.</exception><exception cref="T:System.NotSupportedException">The <see cref="T:System.Collections.Generic.IDictionary`2"/> is read-only.</exception>
        public virtual void Add(TKey key, TValue value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            CheckForAndThrowIfDisposed();

            Add(key, value, IsTrackingChanges);
        }

        /// <summary>
        /// Removes the element with the specified key from the <see cref="T:System.Collections.Generic.IDictionary`2"/>.
        /// </summary>
        /// <returns>
        /// true if the element is successfully removed; otherwise, false.  This method also returns false if <paramref name="key"/> was not found in the original <see cref="T:System.Collections.Generic.IDictionary`2"/>.
        /// </returns>
        /// <param name="key">The key of the element to remove.</param>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="key"/> is null.</exception>
        /// <exception cref="T:System.NotSupportedException">The <see cref="T:System.Collections.Generic.IDictionary`2"/> is read-only.</exception>
        public virtual bool Remove(TKey key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            CheckForAndThrowIfDisposed();

            return Remove(key, IsTrackingChanges);
        }

        /// <summary>
        /// Removes all items from the <see cref="T:System.Collections.Generic.ICollection`1"/> / <see cref="T:System.Collections.IDictionary"/>.
        /// </summary>
        public virtual void Clear()
        {
            CheckForAndThrowIfDisposed();

            var hasHadItemsBeforeClearing = InnerDictionary.Count > 0;
            var valuesBeforeClearing = new List<KeyValuePair<TKey, TValue>>(InnerDictionary);

            InnerDictionary.Clear();

            if (valuesBeforeClearing.Count > 0)
            {
                foreach (var keyValuePair in valuesBeforeClearing)
                {
                    RemoveKeyFromPropertyChangedHandling(keyValuePair.Key);
                    RemoveValueFromPropertyChangedHandling(keyValuePair.Value);
                }
            }

            if (hasHadItemsBeforeClearing)
            {
                NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.Reset(this));
            }
        }

        /// <summary>
        /// Gets the value that is associated with the specified key.
        /// </summary>
        /// <returns>
        /// true if the object that implements the <see cref="T:System.Collections.Generic.IReadOnlyDictionary`2"/> interface contains an element that has the specified key; otherwise, false.
        /// </returns>
        /// <param name="key">The key to locate.</param><param name="value">When this method returns, the value associated with the specified key, if the key is found; otherwise, the default value for the type of the <paramref name="value"/> parameter. This parameter is passed uninitialized.</param><exception cref="T:System.ArgumentNullException"><paramref name="key"/> is null.</exception>
        public virtual bool TryGetValue(TKey key, out TValue value)
        {
            CheckForAndThrowIfDisposed();

            return InnerDictionary.TryGetValue(key, out value);
        }

        /// <summary>
        /// Gets the element that has the specified key in the read-only dictionary.
        /// </summary>
        /// <returns>
        /// The element that has the specified key in the read-only dictionary.
        /// </returns>
        /// <param name="key">The key to locate.</param><exception cref="T:System.ArgumentNullException"><paramref name="key"/> is null.</exception><exception cref="T:System.Collections.Generic.KeyNotFoundException">The property is retrieved and <paramref name="key"/> is not found. </exception>
        public virtual TValue this[TKey key]
        {
            get
            {
                if (key == null) throw new ArgumentNullException(nameof(key));

                CheckForAndThrowIfDisposed();

                return InnerDictionary[key];
            }
            set
            {
                if (key == null) throw new ArgumentNullException(nameof(key));

                CheckForAndThrowIfDisposed();

                AddOrUpdate(key, value);
            }
        }

        /// <summary>
        /// Gets an enumerable collection that contains the keys in the read-only dictionary. 
        /// </summary>
        /// <returns>
        /// An enumerable collection that contains the keys in the read-only dictionary.
        /// </returns>
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys
        {
            get
            {
                CheckForAndThrowIfDisposed();

                return InnerDictionary.Keys;
            }
        }

        /// <summary>
        /// Gets an enumerable collection that contains the values in the read-only dictionary.
        /// </summary>
        /// <returns>
        /// An enumerable collection that contains the values in the read-only dictionary.
        /// </returns>
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values
        {
            get
            {
                CheckForAndThrowIfDisposed();

                return InnerDictionary.Values;
            }
        }

        /// <summary>
        /// Gets the number of elements in the collection.
        /// </summary>
        /// 
        /// <returns>
        /// The number of elements in the collection.
        /// </returns>
        int IReadOnlyCollection<KeyValuePair<TKey, TValue>>.Count
        {
            get
            {
                CheckForAndThrowIfDisposed();

                return Count;
            }
        }

        #endregion

        #region Implementation of INotifyObservableDictionaryItemChanges<out TKey,out TValue>

        /// <summary>
        /// Gets the observable streams of value changes being either a <see cref="ObservableDictionaryChangeType.ItemValueChanged" />
        /// or <see cref="ObservableDictionaryChangeType.ItemValueReplaced" /> event.
        /// </summary>
        /// <value>
        /// The value changes.
        /// </value>
        public virtual IObservable<IObservableDictionaryChange<TKey, TValue>> ValueChanges
        {
            get
            {
                CheckForAndThrowIfDisposed();

                return DictionaryChanges
                    .TakeWhile(_ => !IsDisposing && !IsDisposed)
                    .Where(change => change.ChangeType == ObservableDictionaryChangeType.ItemValueChanged || change.ChangeType == ObservableDictionaryChangeType.ItemValueReplaced)
                    .SkipContinuouslyWhile(change => !IsTrackingItemChanges);
            }
        }

        /// <summary>
        /// Gets the observable streams of key changes that are <see cref="ObservableDictionaryChangeType.ItemKeyChanged" /> events.
        /// </summary>
        /// <value>
        /// The key changes.
        /// </value>
        public virtual IObservable<IObservableDictionaryChange<TKey, TValue>> KeyChanges
        {
            get
            {
                CheckForAndThrowIfDisposed();

                return DictionaryChanges
                    .TakeWhile(_ => !IsDisposing && !IsDisposed)
                    .Where(change => change.ChangeType == ObservableDictionaryChangeType.ItemKeyChanged)
                    .SkipContinuouslyWhile(change => !IsTrackingItemChanges);
            }
        }

        #endregion

        #region Implementation of INotifyObservableCollectionItemChanges<out KeyValuePair<TKey,TValue>>

        /// <summary>
        /// Gets the observable streams of collection item changes.
        /// </summary>
        /// <value>
        /// The item changes.
        /// </value>
        IObservable<IObservableCollectionChange<KeyValuePair<TKey, TValue>>> INotifyObservableCollectionItemChanges<KeyValuePair<TKey, TValue>>.CollectionItemChanges
        {
            get
            {
                CheckForAndThrowIfDisposed();

                return DictionaryChanges
                    .TakeWhile(_ => !IsDisposing && !IsDisposed)
                    .SkipContinuouslyWhile(change => !IsTrackingChanges)
                    .SkipContinuouslyWhile(change => !IsTrackingItemChanges)
                    .Where(change => change.ChangeType == ObservableDictionaryChangeType.ItemKeyChanged
                        || change.ChangeType == ObservableDictionaryChangeType.ItemValueChanged
                        || change.ChangeType == ObservableDictionaryChangeType.ItemValueReplaced)
                    .SelectMany(change => change.ToObservableCollectionChanges(this, ValueComparer));
            }
        }

        #endregion

        #region Implementation of INotifyObservableCollectionChanges<KeyValuePair<TKey,TValue>>

        /// <summary>
        /// Gets the collection changes as an observable stream.
        /// </summary>
        /// <value>
        /// The collection changes.
        /// </value>
        IObservable<IObservableCollectionChange<KeyValuePair<TKey, TValue>>> INotifyObservableCollectionChanges<KeyValuePair<TKey, TValue>>.CollectionChanges
        {
            get
            {
                CheckForAndThrowIfDisposed();

                return DictionaryChanges
                    .TakeWhile(_ => !IsDisposing && !IsDisposed)
                    .SkipContinuouslyWhile(change => !IsTrackingChanges)
                    .SkipContinuouslyWhile(change => change.ChangeType == ObservableDictionaryChangeType.ItemKeyChanged && !IsTrackingItemChanges)
                    .SkipContinuouslyWhile(change => change.ChangeType == ObservableDictionaryChangeType.ItemValueChanged && !IsTrackingItemChanges)
                    .SkipContinuouslyWhile(change => change.ChangeType == ObservableDictionaryChangeType.ItemValueReplaced && !IsTrackingItemChanges)
                    .SkipContinuouslyWhile(change => change.ChangeType == ObservableDictionaryChangeType.Reset && !IsTrackingResets)
                    .SelectMany(dictionaryChange => dictionaryChange.ToObservableCollectionChanges(this, ValueComparer));
            }
        }
        
        #endregion

        #region Implementation of IObservableReadOnlyDictionary<TKey,TValue>

        /// <summary>
        /// Gets a value indicating whether this instance is empty.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is empty; otherwise, <c>false</c>.
        /// </value>
        public virtual bool IsEmpty
        {
            get
            {
                CheckForAndThrowIfDisposed();

                return InnerDictionary.IsEmpty;
            }
        }

        #endregion

        #region Implementation of ICollection<KeyValuePair<TKey,TValue>>

        /// <summary>
        /// Gets a value indicating whether this instance is read only.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is read only; otherwise, <c>false</c>.
        /// </value>
        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly
        {
            get
            {
                CheckForAndThrowIfDisposed();

                return ((ICollection<KeyValuePair<TKey, TValue>>)InnerDictionary).IsReadOnly;
            }
        }

        /// <summary>
        /// Gets the number of elements in the collection.
        /// </summary>
        /// <returns>
        /// The number of elements in the collection. 
        /// </returns>
        int ICollection<KeyValuePair<TKey, TValue>>.Count
        {
            get
            {
                CheckForAndThrowIfDisposed();

                return Count;
            }
        }

        /// <summary>
        /// Adds an item to the <see cref="T:System.Collections.Generic.ICollection`1"/>.
        /// </summary>
        /// <param name="item">The object to add to the <see cref="T:System.Collections.Generic.ICollection`1"/>.</param><exception cref="T:System.NotSupportedException">The <see cref="T:System.Collections.Generic.ICollection`1"/> is read-only.</exception>
        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
        {
            CheckForAndThrowIfDisposed();

            Add(item.Key, item.Value);
        }

        /// <summary>
        /// Determines whether the <see cref="T:System.Collections.Generic.ICollection`1"/> contains a specific value.
        /// </summary>
        /// <returns>
        /// true if <paramref name="item"/> is found in the <see cref="T:System.Collections.Generic.ICollection`1"/>; otherwise, false.
        /// </returns>
        /// <param name="item">The object to locate in the <see cref="T:System.Collections.Generic.ICollection`1"/>.</param>
        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
        {
            CheckForAndThrowIfDisposed();

            TValue value;
            if (!this.TryGetValue(item.Key, out value))
                return false;

            return ValueComparer.Equals(value, item.Value);
        }

        /// <summary>
        /// Copies the elements of the <see cref="T:System.Collections.Generic.ICollection`1"/> to an <see cref="T:System.Array"/>, starting at a particular <see cref="T:System.Array"/> index.
        /// </summary>
        /// <param name="array">The one-dimensional <see cref="T:System.Array"/> that is the destination of the elements copied from <see cref="T:System.Collections.Generic.ICollection`1"/>. The <see cref="T:System.Array"/> must have zero-based indexing.</param><param name="arrayIndex">The zero-based index in <paramref name="array"/> at which copying begins.</param><exception cref="T:System.ArgumentNullException"><paramref name="array"/> is null.</exception><exception cref="T:System.ArgumentOutOfRangeException"><paramref name="arrayIndex"/> is less than 0.</exception><exception cref="T:System.ArgumentException">The number of elements in the source <see cref="T:System.Collections.Generic.ICollection`1"/> is greater than the available space from <paramref name="arrayIndex"/> to the end of the destination <paramref name="array"/>.</exception>
        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            CheckForAndThrowIfDisposed();

            ((ICollection<KeyValuePair<TKey, TValue>>)InnerDictionary).CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Removes the first occurrence of a specific object from the <see cref="T:System.Collections.Generic.ICollection`1"/>.
        /// </summary>
        /// <returns>
        /// true if <paramref name="item"/> was successfully removed from the <see cref="T:System.Collections.Generic.ICollection`1"/>; otherwise, false. This method also returns false if <paramref name="item"/> is not found in the original <see cref="T:System.Collections.Generic.ICollection`1"/>.
        /// </returns>
        /// <param name="item">The object to remove from the <see cref="T:System.Collections.Generic.ICollection`1"/>.</param><exception cref="T:System.NotSupportedException">The <see cref="T:System.Collections.Generic.ICollection`1"/> is read-only.</exception>
        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            CheckForAndThrowIfDisposed();

            var wasRemoved = ((ICollection<KeyValuePair<TKey, TValue>>)InnerDictionary).Remove(item);
            if (wasRemoved)
            {
                RemoveKeyFromPropertyChangedHandling(item.Key);
                RemoveValueFromPropertyChangedHandling(item.Value);

                NotifyObserversAboutDictionaryChanges(ObservableDictionaryChange<TKey, TValue>.ItemRemoved(this, item.Key, item.Value));
            }

            return wasRemoved;
        }

        #endregion

        #region Implementation of IBulkModifiableDictionary<TKey,TValue>

        /// <summary>
        /// Adds a range of items.
        /// </summary>
        /// <param name="items">The items to add.</param>
        public virtual void AddRange(IEnumerable<KeyValuePair<TKey, TValue>> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            CheckForAndThrowIfDisposed();

            var itemsAsList = items.ToList();

            if (itemsAsList.Count == 0)
                return;

            IDictionary<TKey, TValue> itemsThatCouldNotBeAdded;
            if (TryAddRange(itemsAsList, out itemsThatCouldNotBeAdded) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(items),
                    $"The following key(s) are already in this dictionary and cannot be added to it: {string.Join(", ", itemsThatCouldNotBeAdded.Select(item => item.Key.ToString()))}");
            }
        }

        /// <summary>
        /// Removes the specified items.
        /// </summary>
        /// <param name="items">The items to remove.</param>
        public virtual void RemoveRange(IEnumerable<KeyValuePair<TKey, TValue>> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            CheckForAndThrowIfDisposed();

            var itemsAsList = items.ToList();

            if (itemsAsList.Count == 0)
                return;

            IDictionary<TKey, TValue> itemsThatCouldNotBeRemoved;
            if (TryRemoveRange(itemsAsList, out itemsThatCouldNotBeRemoved) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(items),
                    $"The following key/value pair(s) are not in this dictionary and cannot be removed from it: {string.Join(", ", itemsThatCouldNotBeRemoved.Select(item => item.ToString()))}");
            }
        }

        /// <summary>
        /// Removes the items for the provided <paramref name="keys"/>.
        /// </summary>
        /// <param name="keys">The keys.</param>
        public virtual void RemoveRange(IEnumerable<TKey> keys)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));

            CheckForAndThrowIfDisposed();

            var keysAsList = keys.ToList();

            if (keysAsList.Count == 0)
                return;

            IList<TKey> keysThatCouldNotBeRemoved;
            if (TryRemoveRange(keysAsList, out keysThatCouldNotBeRemoved) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(keys),
                    $"The following key(s) are not in this dictionary and cannot be removed from it: {string.Join(", ", keysThatCouldNotBeRemoved.Select(item => item.ToString()))}");
            }
        }

        #endregion
    }
}