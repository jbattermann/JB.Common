﻿// -----------------------------------------------------------------------
// <copyright file="ObservableCollectionChange.cs" company="Joerg Battermann">
//   Copyright (c) 2015 Joerg Battermann. All rights reserved.
// </copyright>
// <author>Joerg Battermann</author>
// <summary></summary>
// -----------------------------------------------------------------------

using System;

namespace JB.Collections.Reactive
{
    public class ObservableCollectionChange<T> : IObservableCollectionChange<T>
    {
        /// <summary>
        /// The type is a value type.. or not.. let's find out, lazily.
        /// </summary>
        private static readonly Lazy<bool> TypeIsValueType = new Lazy<bool>(() => typeof(T).IsValueType);

        #region Implementation of IObservableCollectionChange<out T>

        /// <summary>
        /// Gets the type of the change.
        /// </summary>
        /// <value>
        /// The type of the change.
        /// </value>
        public ObservableCollectionChangeType ChangeType { get; }

        /// <summary>
        /// Gets the new, post-change (starting) index for the <see cref="Item"/>.
        /// </summary>
        /// <value>
        /// The post-change starting index, -1 for removals, otherwise 0 or greater.
        /// </value>
        public int Index { get; }

        /// <summary>
        /// Gets the previous, pre-change (starting) index for the <see cref="Item"/>.
        /// </summary>
        /// <value>
        /// The pre-change (starting) index, -1 for additions, otherwise 0 or greater.
        /// </value>
        public int OldIndex { get; }

        /// <summary>
        /// Gets the items that were changed or removed.
        /// </summary>
        /// <value>
        /// The affected items.
        /// </value>
        public T Item { get; }

        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="ObservableCollectionChange{T}" /> class.
        /// </summary>
        /// <param name="changeType">Type of the change.</param>
        /// <param name="item">The item.</param>
        /// <param name="index">New starting index, after the add, change or move, -1 of not applicable.</param>
        /// <param name="oldIndex">Old starting index, before the add, change or move, -1 of not applicable.</param>
        public ObservableCollectionChange(ObservableCollectionChangeType changeType, T item = default(T), int index = -1, int oldIndex = -1)
        {
            if (index < -1) throw new ArgumentOutOfRangeException(nameof(index), "Value cannot be less than -1");
            if (oldIndex < -1) throw new ArgumentOutOfRangeException(nameof(oldIndex), "Value cannot be less than -1");

            if (changeType == ObservableCollectionChangeType.ItemAdded && index == -1)
                throw new ArgumentOutOfRangeException(nameof(index), $"Item adds must not have an {nameof(index)} of -1.");

            if (changeType == ObservableCollectionChangeType.ItemAdded && oldIndex != -1)
                throw new ArgumentOutOfRangeException(nameof(oldIndex), $"Item adds must have an {nameof(oldIndex)} of -1.");

            if (changeType == ObservableCollectionChangeType.ItemRemoved && index != -1)
                throw new ArgumentOutOfRangeException(nameof(index), $"Item removals must have an {nameof(index)} of -1.");

            if (changeType == ObservableCollectionChangeType.ItemRemoved && oldIndex == -1)
                throw new ArgumentOutOfRangeException(nameof(oldIndex), $"Item removals must nothave an {nameof(oldIndex)} of -1.");

            if (changeType == ObservableCollectionChangeType.ItemMoved && index == -1)
                throw new ArgumentOutOfRangeException(nameof(index), $"Item moves must not have an {nameof(index)} of -1.");

            if (changeType == ObservableCollectionChangeType.ItemMoved && oldIndex == -1)
                throw new ArgumentOutOfRangeException(nameof(oldIndex), $"Item moves must not have an {nameof(oldIndex)} of -1.");

            if (changeType == ObservableCollectionChangeType.ItemChanged && index == -1)
                throw new ArgumentOutOfRangeException(nameof(index), $"Item changes must not have an {nameof(index)} of -1 but the index of the changed item.");

            if (changeType == ObservableCollectionChangeType.ItemChanged && oldIndex != index)
                throw new ArgumentOutOfRangeException(nameof(index), $"Item changes must have the same index position for both, {nameof(index)} and {nameof(oldIndex)}.");

            if (changeType == ObservableCollectionChangeType.Reset && index != -1)
                throw new ArgumentOutOfRangeException(nameof(index), $"Resets must have an {nameof(index)} of -1.");

            if (changeType == ObservableCollectionChangeType.Reset && oldIndex != -1)
                throw new ArgumentOutOfRangeException(nameof(oldIndex), $"Resets must have an {nameof(oldIndex)} of -1.");

            if (changeType == ObservableCollectionChangeType.Reset && (TypeIsValueType.Value == false && !Equals(item, default(T))))
                throw new ArgumentOutOfRangeException(nameof(item), $"Resets must not have an {nameof(item)}");

            if ((changeType == ObservableCollectionChangeType.ItemAdded
                || changeType == ObservableCollectionChangeType.ItemChanged
                || changeType == ObservableCollectionChangeType.ItemMoved
                || changeType == ObservableCollectionChangeType.ItemRemoved)
                && (TypeIsValueType.Value == false && Equals(item, default(T))))
                throw new ArgumentOutOfRangeException(nameof(item), $"Item Adds, Changes, Moves and Removes must have an {nameof(item)}");

            ChangeType = changeType;
            Item = item;

            Index = index;
            OldIndex = oldIndex;
        }

        /// <summary>
        /// Gets a <see cref="IObservableCollectionChange{T}"/> representing a <see cref="ObservableCollectionChangeType.Reset"/>.
        /// </summary>
        /// <value>
        /// The reset.
        /// </value>
        public static IObservableCollectionChange<T> Reset => new ObservableCollectionChange<T>(ObservableCollectionChangeType.Reset);
    }
}