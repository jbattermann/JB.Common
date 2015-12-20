﻿using System;

namespace JB.Collections.Reactive.ExtensionMethods
{
    /// <summary>
    /// Extension methods for <see cref="IObservableDictionaryChange{TKey,TValue}"/> instances.
    /// </summary>
    public static class ObservableDictionaryChangeExtensions
    {
        /// <summary>
        /// Converts the given <paramref name="observableDictionaryChange"/> to its <see cref="IObservableCollectionChange{T}"/> counterpart.
        /// </summary>
        /// <typeparam name="TKey">The type of the key.</typeparam>
        /// <typeparam name="TValue">The type of the value.</typeparam>
        /// <param name="observableDictionaryChange">The observable dictionary change.</param>
        /// <returns></returns>
        /// <exception cref="System.ArgumentNullException"></exception>
        /// <exception cref="System.ArgumentOutOfRangeException">$Only {ObservableDictionaryChangeType.ItemAdded}, {ObservableDictionaryChangeType.ItemChanged}, {ObservableDictionaryChangeType.ItemRemoved} and {ObservableDictionaryChangeType.Reset} are supported.</exception>
        public static IObservableCollectionChange<TValue> ToObservableCollectionChange<TKey, TValue>(this IObservableDictionaryChange<TKey, TValue> observableDictionaryChange)
        {
            if (observableDictionaryChange == null) throw new ArgumentNullException(nameof(observableDictionaryChange));
        
            switch (observableDictionaryChange.ChangeType)
            {
                case ObservableDictionaryChangeType.ItemAdded:
                    return new ObservableCollectionChange<TValue>(
                        ObservableCollectionChangeType.ItemAdded,
                        observableDictionaryChange.Value);
                case ObservableDictionaryChangeType.ItemChanged:
                    return new ObservableCollectionChange<TValue>(
                        ObservableCollectionChangeType.ItemChanged,
                        observableDictionaryChange.Value);
                case ObservableDictionaryChangeType.ItemRemoved:
                    return new ObservableCollectionChange<TValue>(
                        ObservableCollectionChangeType.ItemRemoved,
                        observableDictionaryChange.Value);
                case ObservableDictionaryChangeType.Reset:
                    return new ObservableCollectionChange<TValue>(
                        ObservableCollectionChangeType.Reset);
                default:
                    throw new ArgumentOutOfRangeException(nameof(observableDictionaryChange),
                        $"Only {ObservableDictionaryChangeType.ItemAdded}, {ObservableDictionaryChangeType.ItemChanged}, {ObservableDictionaryChangeType.ItemRemoved} and {ObservableDictionaryChangeType.Reset} are supported.");
            }
        }
    }
}