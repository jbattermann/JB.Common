using System;

namespace JB.Collections.Reactive
{
    public interface INotifyObservableDictionaryChanges<TKey, TValue> : INotifyObservableChanges
    {
        /// <summary>
        /// Gets the dictionary changes as an observable stream.
        /// </summary>
        /// <value>
        /// The dictionary changes.
        /// </value>
        IObservable<IObservableDictionaryChange<TKey, TValue>> DictionaryChanges { get; }
    }
}