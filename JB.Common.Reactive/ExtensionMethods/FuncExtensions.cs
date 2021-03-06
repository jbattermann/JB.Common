﻿// -----------------------------------------------------------------------
// <copyright file="FuncExtensions.cs" company="Joerg Battermann">
//   Copyright (c) 2017 Joerg Battermann. All rights reserved.
// </copyright>
// <author>Joerg Battermann</author>
// <summary></summary>
// -----------------------------------------------------------------------

using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace JB.Reactive.ExtensionMethods
{
    /// <summary>
    /// Extension methods for <see cref="Func{TResult}"/> instances
    /// </summary>
    public static class FuncExtensions
    {
        /// <summary>
        /// Returns an observable sequence that invokes the <paramref name="func"/> synchronously upon subscription and returns its result.
        /// </summary>
        /// <param name="func">Function to run on subscription.</param>
        /// <param name="scheduler">Scheduler to run the <paramref name="func"/> on.</param>
        /// <returns>
        /// An observable sequence exposing the result value upon completion of the given <paramref name="func"/>, or an exception if one occured.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="func"/> is null.</exception>
        public static IObservable<TResult> ToObservable<TResult>(this Func<TResult> func, IScheduler scheduler = null)
        {
            if (func == null)
                throw new ArgumentNullException(nameof(func));

            return scheduler != null
                ? Observable.Start(func, scheduler)
                : Observable.Start(func);
        }
    }
}