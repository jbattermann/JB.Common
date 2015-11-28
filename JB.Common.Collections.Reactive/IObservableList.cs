﻿// -----------------------------------------------------------------------
// <copyright file="IObservableList.cs" company="Joerg Battermann">
//   Copyright (c) 2015 Joerg Battermann. All rights reserved.
// </copyright>
// <author>Joerg Battermann</author>
// <summary></summary>
// -----------------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;

namespace JB.Collections.Reactive
{
	public interface IObservableList<T> : IObservableCollection<T>, IList<T>, ICollection<T>, IEnumerable<T>,
		ICollection, IEnumerable, IList
    {
		 
	}
}