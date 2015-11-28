using System;
using System.Reactive.Subjects;
using JB.Reactive.Analytics.AnalysisResults;
using JB.Reactive.Analytics.Analyzers;

namespace JB.Reactive.Analytics.Providers
{
    /// <summary>
    /// Base interface that allows <see cref="IAnalyzer{TSource}"/>s to be registered for
    /// analysis of <typeparamref name="TSource"/> elements and produces a corresponding output
    /// sequence of <see cref="IAnalysisResult"/> instances.
    /// </summary>
    /// <typeparam name="TSource">The type of the source.</typeparam>
    public interface IAnalyticsProvider<TSource> : ISubject<TSource>
    {
        /// <summary>
        /// Gets the analysis results observable.
        /// </summary>
        /// <value>
        /// The analysis results.
        /// </value>
        IObservable<IAnalysisResult> AnalysisResults { get; }

        /// <summary>
        /// Registers an analyzer with this instance that will be used in all future instance of the <typeparam name="TSource"/> sequence.
        /// </summary>
        /// <param name="analyzer">The analyzer.</param>
        void RegisterAnalyzer(IAnalyzer<TSource> analyzer);

        /// <summary>
        /// De-registers the analyzer, does not affect currently ongoing analyses.
        /// </summary>
        /// <param name="analyzer">The analyzer.</param>
        void DeregisterAnalyzer(IAnalyzer<TSource> analyzer);
    }

    /// <summary>
    /// An <see cref="IAnalyticsProvider{TSource}"/> that only provides a specific <see cref="IAnalysisResult"/> sequence.
    /// </summary>
    /// <typeparam name="TSource">The type of the source.</typeparam>
    /// <typeparam name="TAnalysisResult">The type of the analysis result.</typeparam>
    public interface IAnalyticsProvider<TSource, TAnalysisResult> : ISubject<TSource>
        where TAnalysisResult : IAnalysisResult
    {
        /// <summary>
        /// Gets the analysis results observable.
        /// </summary>
        /// <value>
        /// The analysis results.
        /// </value>
        IObservable<TAnalysisResult> AnalysisResults { get; }

        /// <summary>
        /// Registers an analyzer with this instance that will be used in all future instance of the <typeparam name="TSource"/> sequence.
        /// </summary>
        /// <param name="analyzer">The analyzer.</param>
        void RegisterAnalyzer(IAnalyzer<TSource, TAnalysisResult> analyzer);

        /// <summary>
        /// De-registers the analyzer, does not affect currently ongoing analyses.
        /// </summary>
        /// <param name="analyzer">The analyzer.</param>
        void DeregisterAnalyzer(IAnalyzer<TSource, TAnalysisResult> analyzer);
    }