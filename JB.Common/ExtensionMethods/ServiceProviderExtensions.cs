﻿// -----------------------------------------------------------------------
// <copyright file="ServiceProviderExtensions.cs" company="Joerg Battermann">
//   Copyright (c) 2015 Joerg Battermann. All rights reserved.
// </copyright>
// <author>Joerg Battermann</author>
// <summary></summary>
// -----------------------------------------------------------------------

using System;

namespace JB.ExtensionMethods
{
	/// <summary>
	/// Extension Methods for <see cref="IServiceProvider"/> instances.
	/// </summary>
	public static class ServiceProviderExtensions
	{
		/// <summary>
		///     Gets the service of the specific type.
		/// </summary>
		/// <typeparam name="TService">Type of the service</typeparam>
		/// <returns></returns>
		public static TService GetService<TService>(this IServiceProvider serviceProvider)
        {
			if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));
            
            return (TService)serviceProvider.GetService(typeof(TService));
		}

        /// <summary>
        ///     Gets the service of the specific type for a given, known implementation.
        /// </summary>
        /// <typeparam name="TService">The type of the service interface.</typeparam>
        /// <typeparam name="TKnownServiceImplementation">The known type of the service implementation.</typeparam>
        /// <returns></returns>
        public static TService GetService<TService, TKnownServiceImplementation>(this IServiceProvider serviceProvider)
            where TService : class
            where TKnownServiceImplementation : TService
		{
			if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));

            return serviceProvider.GetService(typeof(TKnownServiceImplementation)) as TService;
        }
	}
}