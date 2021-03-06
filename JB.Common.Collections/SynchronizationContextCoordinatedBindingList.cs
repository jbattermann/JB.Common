﻿using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using JB.Threading;

namespace JB.Collections
{
	/// <summary>
	///     An <see cref="IBindingList" /> implementation that's raising its <see cref="BindingList{T}.AddingNew"/> and <see cref="BindingList{T}.ListChanged"/> events
	///		on the provided or <see cref="SynchronizationContext.Current">constructor created</see> <see cref="SynchronizationContext" />.
	/// </summary>
	/// <typeparam name="T">The type of elements in the list.</typeparam>
	public class SynchronizationContextCoordinatedBindingList<T> : EnhancedBindingList<T>
	{
		private readonly SynchronizationContext _synchronizationContext;

		/// <summary>
		/// Initializes a new instance of the <see cref="SynchronizationContextCoordinatedBindingList{T}" /> class.
		/// </summary>
		/// <param name="list">An <see cref="T:System.Collections.Generic.IList`1" /> of items to be contained in the
		/// <see cref="T:System.ComponentModel.BindingList`1" />.</param>
		/// <param name="synchronizationContext">The synchronization context.</param>
		public SynchronizationContextCoordinatedBindingList(IList<T> list = null, SynchronizationContext synchronizationContext = null)
			: base(list)
		{
			_synchronizationContext = synchronizationContext ?? (SynchronizationContext.Current ?? new SynchronizationContext());
		}

		#region Overrides of BindingList<T>

		/// <summary>
		///     Raises the <see cref="E:System.ComponentModel.BindingList`1.AddingNew" /> event.
		/// </summary>
		/// <param name="addingNewEventArgs">An <see cref="T:System.ComponentModel.AddingNewEventArgs" /> that contains the event data. </param>
		protected override void OnAddingNew(AddingNewEventArgs addingNewEventArgs)
		{
			_synchronizationContext.Send(() => base.OnAddingNew(addingNewEventArgs));
		}

		/// <summary>
		///     Raises the <see cref="E:System.ComponentModel.BindingList`1.ListChanged" /> event.
		/// </summary>
		/// <param name="listChangedEventArgs">A <see cref="T:System.ComponentModel.ListChangedEventArgs" /> that contains the event data. </param>
		protected override void OnListChanged(ListChangedEventArgs listChangedEventArgs)
		{
			_synchronizationContext.Send(() => base.OnListChanged(listChangedEventArgs));
		}

		#endregion
	}
}