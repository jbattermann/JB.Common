// -----------------------------------------------------------------------
// <copyright file="ObservableDictionaryNotificationTests.cs" company="Joerg Battermann">
//   Copyright (c) 2017 Joerg Battermann. All rights reserved.
// </copyright>
// <author>Joerg Battermann</author>
// <summary></summary>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reactive;
using FluentAssertions;
using Microsoft.Reactive.Testing;
using Xunit;

namespace JB.Collections.Reactive.Tests
{
    public class ObservableDictionaryNotificationTests
    {
        [Fact]
        public void DictionaryChangesObserverExceptionsShouldNotBeThrownIfHandledViaUnhandledObserverExceptionsObservable()
        {
            // given
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                observableDictionary.ObserverExceptions.Subscribe(observerException =>
                {
                    observerException.Handled = true;
                });

                observableDictionary.DictionaryChanges.Subscribe(_ =>
                {
                    throw new InvalidOperationException("My Marker Message");
                });

                // when
                Action action = () => observableDictionary.Add(1, "One");

                // then
                action.Should().NotThrow<InvalidOperationException>();
            }
        }

        [Fact]
        public void DictionaryChangesObserverExceptionsShouldBeThrownIfUnhandled()
        {
            // given
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                observableDictionary.DictionaryChanges.Subscribe(_ =>
                {
                    throw new InvalidOperationException("My Marker Message");
                });

                // when
                Action action = () => observableDictionary.Add(1, "One");

                // then
                action
                    .Should().Throw<InvalidOperationException>()
                    .WithMessage("My Marker Message");
            }
        }

        [Fact]
        public void CountChangesObserverExceptionsShouldNotBeThrownIfHandledViaUnhandledObserverExceptionsObservable()
        {
            // given
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                observableDictionary.ObserverExceptions.Subscribe(observerException =>
                {
                    observerException.Handled = true;
                });

                observableDictionary.CountChanges.Subscribe(_ =>
                {
                    throw new InvalidOperationException("My Marker Message");
                });

                // when
                Action action = () => observableDictionary.Add(1, "One");

                // then
                action.Should().NotThrow<InvalidOperationException>();
            }
        }

        [Fact]
        public void CountChangesObserverExceptionsShouldBeThrownIfUnhandled()
        {
            // given
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                observableDictionary.CountChanges.Subscribe(_ =>
                {
                    throw new InvalidOperationException("My Marker Message");
                });

                // when
                Action action = () => observableDictionary.Add(1, "One");

                // then
                action
                    .Should().Throw<InvalidOperationException>()
                    .WithMessage("My Marker Message");
            }
        }
        
        [Fact]
        public void CollectionChangedSubscriberExceptionsShouldNotBeThrownIfHandledViaUnhandledObserverExceptionsObservable()
        {
            // given
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                observableDictionary.ObserverExceptions.Subscribe(observerException =>
                {
                    observerException.Handled = true;
                });

                ((INotifyCollectionChanged)observableDictionary).CollectionChanged
                    += (sender, args) => { throw new InvalidOperationException("My Marker Message"); };

                // when
                Action action = () => observableDictionary.Add(1, "One");

                // then
                action.Should().NotThrow<InvalidOperationException>();
            }
        }

        [Fact]
        public void CollectionChangedSubscriberExceptionsShouldBeThrownIfUnhandled()
        {
            // given
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                ((INotifyCollectionChanged)observableDictionary).CollectionChanged
                    += (sender, args) => { throw new InvalidOperationException("My Marker Message"); };

                // when
                Action action = () => observableDictionary.Add(1, "One");

                // then
                action
                    .Should().Throw<InvalidOperationException>()
                    .WithMessage("My Marker Message");
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        public void AddNotifiesAdditionAsResetIfRequested(int amountOfItemsToAdd)
        {
            // given
            var scheduler = new TestScheduler();
            var dictionaryChangesObserver = scheduler.CreateObserver<IObservableDictionaryChange<int, string>>();
            var resetsObserver = scheduler.CreateObserver<Unit>();

            using (var observableDictionary = new ObservableDictionary<int, string>(scheduler: scheduler))
            {
                // when
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = 0;

                using (observableDictionary.DictionaryChanges.Subscribe(dictionaryChangesObserver))
                {
                    using (observableDictionary.Resets.Subscribe(resetsObserver))
                    {
                        var addedKeyValuePairs = new List<KeyValuePair<int, string>>();
                        for (int i = 0; i < amountOfItemsToAdd; i++)
                        {
                            var keyValuePair = new KeyValuePair<int, string>(i, $"#{i}");

                            observableDictionary.Add(keyValuePair.Key, keyValuePair.Value);
                            addedKeyValuePairs.Add(keyValuePair);

                            scheduler.AdvanceBy(2);
                        }

                        // then
                        observableDictionary.Count.Should().Be(amountOfItemsToAdd);

                        dictionaryChangesObserver.Messages.Count.Should().Be(amountOfItemsToAdd);
                        resetsObserver.Messages.Count.Should().Be(amountOfItemsToAdd);

                        if (amountOfItemsToAdd > 0)
                        {
                            dictionaryChangesObserver.Messages.Select(message => message.Value.Value.ChangeType).Should().OnlyContain(changeType => changeType == ObservableDictionaryChangeType.Reset);

                            dictionaryChangesObserver.Messages.Select(message => message.Value.Value.Key).Should().Match(ints => ints.All(@int => Equals(default(int), @int)));
                            dictionaryChangesObserver.Messages.Select(message => message.Value.Value.Value).Should().Match(strings => strings.All(@string => Equals(default(string), @string)));
                        }
                    }
                }
            }
        }

        [Theory]
        [InlineData(10, 10)]
        [InlineData(10, 100)]
        [InlineData(5, 1000)]
        public void AddNotifiesCountIncrease(int lowerLimit, int upperLimit)
        {
            // given
            var initialList = new List<KeyValuePair<int, string>>()
            {
                new KeyValuePair<int, string>(1, "Some Value"),
                new KeyValuePair<int, string>(2, "Some Other Value"),
                new KeyValuePair<int, string>(3, "Some Totally Different Value"),
            };

            int observableReportedCount = initialList.Count;
            int countChangesCalled = 0;

            using (var observableDictionary = new ObservableDictionary<int, string>(initialList))
            {
                // when
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = int.MaxValue;
                observableDictionary.CountChanges.Subscribe(i =>
                {
                    observableReportedCount = i;
                    countChangesCalled++;
                });

                for (int i = lowerLimit; i <= upperLimit; i++)
                {
                    observableDictionary.Add(i, $"#{i}");
                }

                // then check whether all items have been accounted for
                var expectedCountChangesCalls = ((upperLimit == lowerLimit) ? 1 : (upperLimit - lowerLimit + 1));
                var expectedCount = expectedCountChangesCalls + initialList.Count;

                observableReportedCount.Should().Be(expectedCount); // +1 because the upper for loop goes up to & inclusive the upperLimit
                observableReportedCount.Should().Be(observableDictionary.Count);

                countChangesCalled.Should().Be(expectedCountChangesCalls);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        public void AddNotifiesItemAddition(int amountOfItemsToAdd)
        {
            // given
            var scheduler = new TestScheduler();
            var observer = scheduler.CreateObserver<IObservableDictionaryChange<int, string>>();

            using (var observableDictionary = new ObservableDictionary<int, string>(scheduler: scheduler))
            {
                // when
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = int.MaxValue;

                using (observableDictionary.DictionaryChanges.Subscribe(observer))
                {
                    var addedKeyValuePairs = new List<KeyValuePair<int, string>>();
                    for (int i = 0; i < amountOfItemsToAdd; i++)
                    {
                        var keyValuePair = new KeyValuePair<int, string>(i, $"#{i}");

                        observableDictionary.Add(keyValuePair.Key, keyValuePair.Value);
                        addedKeyValuePairs.Add(keyValuePair);

                        scheduler.AdvanceBy(1);
                    }

                    // then
                    observableDictionary.Count.Should().Be(amountOfItemsToAdd);
                    observer.Messages.Count.Should().Be(amountOfItemsToAdd);

                    if (amountOfItemsToAdd > 0)
                    {
                        observer.Messages.Select(message => message.Value.Value.ChangeType).Should().OnlyContain(changeType => changeType == ObservableDictionaryChangeType.ItemAdded);

                        observer.Messages.Select(message => message.Value.Value.Key).Should().Contain(addedKeyValuePairs.Select(kvp => kvp.Key));
                        observer.Messages.Select(message => message.Value.Value.Value).Should().Contain(addedKeyValuePairs.Select(kvp => kvp.Value));
                    }
                }
            }
        }

        [Fact]
        public void AddRaisesPropertyChangedEventForItemIndexerdAndCount()
        {
            // given
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                using (var monitoredObservableDictionary = observableDictionary.Monitor())
                {
                    // when
                    observableDictionary.Add(1, "One");

                    // then
                    monitoredObservableDictionary
                        .Should().Raise(nameof(observableDictionary.PropertyChanged))
                        .WithSender(observableDictionary)
                        .WithArgs<PropertyChangedEventArgs>(args => args.PropertyName == "Item[]");

                    monitoredObservableDictionary
                        .Should().Raise(nameof(observableDictionary.PropertyChanged))
                        .WithSender(observableDictionary)
                        .WithArgs<PropertyChangedEventArgs>(args => args.PropertyName == nameof(observableDictionary.Count));
                }
            }
        }

        [Fact(Skip = "Count is currently not captured / filtered properly by FluentAssertions")]
        public void AddRangeRaisesPropertyChangeEventForItemIndexerdAndCountOnlyOnceWhenThresholdAmountWhenItemChangesAreNotifiedAsResetIsSetAccordingly()
        {
            // given
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = 0;

                var items = new List<KeyValuePair<int, string>>()
                {
                    new KeyValuePair<int, string>(1, "One"),
                    new KeyValuePair<int, string>(2, "Two"),
                    new KeyValuePair<int, string>(3, "Three"),
                };

                using (var monitoredObservableDictionary = observableDictionary.Monitor())
                {
                    // when
                    observableDictionary.AddRange(items);

                    // then
                    monitoredObservableDictionary
                        .Should().Raise(nameof(observableDictionary.PropertyChanged))
                        .WithSender(observableDictionary)
                        .WithArgs<PropertyChangedEventArgs>(args => args.PropertyName == "Item[]")
                        .Should().HaveCount(1); // ToDo: re-enable test if/when https://github.com/dennisdoomen/fluentassertions/issues/337 has been fixed

                    monitoredObservableDictionary
                        .Should().Raise(nameof(observableDictionary.PropertyChanged))
                        .WithSender(observableDictionary)
                        .WithArgs<PropertyChangedEventArgs>(args => args.PropertyName == nameof(observableDictionary.Count))
                        .Should().HaveCount(1); // ToDo: re-enable test if/when https://github.com/dennisdoomen/fluentassertions/issues/337 has been fixed
                }
            }
        }

        [Fact]
        public void ClearRaisesPropertyChangeEventForItemIndexerdAndCount()
        {
            // given
            var items = new List<KeyValuePair<int, string>>()
                {
                    new KeyValuePair<int, string>(1, "One"),
                    new KeyValuePair<int, string>(2, "Two"),
                    new KeyValuePair<int, string>(3, "Three"),
                };

            using (var observableDictionary = new ObservableDictionary<int, string>(items))
            {
                using(var monitoredObservableDictionary = observableDictionary.Monitor())
                {
                    // when
                    observableDictionary.Clear();

                    // then
                    monitoredObservableDictionary
                        .Should().Raise(nameof(observableDictionary.PropertyChanged))
                        .WithSender(observableDictionary)
                        .WithArgs<PropertyChangedEventArgs>(args => args.PropertyName == "Item[]");

                    monitoredObservableDictionary
                        .Should().Raise(nameof(observableDictionary.PropertyChanged))
                        .WithSender(observableDictionary)
                        .WithArgs<PropertyChangedEventArgs>(args => args.PropertyName == nameof(observableDictionary.Count));
                }
            }
        }

        [Fact]
        public void ClearDoesNotRaisesPropertyChangeEventForItemIndexerdAndCountIfDictionaryIsEmpty()
        {
            // given
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                using(var monitoredObservableDictionary = observableDictionary.Monitor())
                {
                    // when
                    observableDictionary.Clear();

                    // then
                    monitoredObservableDictionary
                        .Should().NotRaise(nameof(observableDictionary.PropertyChanged));
                }
            }
        }

        [Fact]
        public void ResetRaisesPropertyChangeEventForItemIndexerdAndCount()
        {
            // given
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                using (var monitoredObservableDictionary = observableDictionary.Monitor())
                {
                    // when
                    observableDictionary.Reset();

                    // then
                    monitoredObservableDictionary
                        .Should().Raise(nameof(observableDictionary.PropertyChanged))
                        .WithSender(observableDictionary)
                        .WithArgs<PropertyChangedEventArgs>(args => args.PropertyName == "Item[]");

                    monitoredObservableDictionary
                        .Should().Raise(nameof(observableDictionary.PropertyChanged))
                        .WithSender(observableDictionary)
                        .WithArgs<PropertyChangedEventArgs>(args => args.PropertyName == nameof(observableDictionary.Count));
                }
            }
        }

        [Fact]
        public void AddRangeNotifiesCountChangeOnceIfAddedItemsAreMoreThanThreshold()
        {
            // given
            var testScheduler = new TestScheduler();
            var countChangesObserver = testScheduler.CreateObserver<int>();

            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = 0;
                observableDictionary.CountChanges.Subscribe(countChangesObserver);

                var items = Enumerable.Range(0, 100).ToDictionary(i => i, i => i.ToString());

                // when
                observableDictionary.AddRange(items);

                // then
                countChangesObserver.Messages.Count.Should().Be(1);
            }
        }

        [Fact]
        public void AddRangeNotifiesCountChangeForEveryAddedItemIfAddedItemsAreLessThanThreshold()
        {
            // given
            var testScheduler = new TestScheduler();
            var countChangesObserver = testScheduler.CreateObserver<int>();

            int itemsAmountToAdd = 100;
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = itemsAmountToAdd + 1;
                observableDictionary.CountChanges.Subscribe(countChangesObserver);

                var items = Enumerable.Range(0, itemsAmountToAdd).ToDictionary(i => i, i => i.ToString());

                // when
                observableDictionary.AddRange(items);

                // then
                countChangesObserver.Messages.Count.Should().Be(itemsAmountToAdd);
            }
        }

        [Fact]
        public void AddRangeRaisesPropertyChangeEventForItemIndexerAndCount()
        {
            // given
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                var items = new List<KeyValuePair<int, string>>()
                {
                    new KeyValuePair<int, string>(1, "One"),
                    new KeyValuePair<int, string>(2, "Two"),
                    new KeyValuePair<int, string>(3, "Three"),
                };

                using (var monitoredObservableDictionary = observableDictionary.Monitor())
                {
                    // when
                    observableDictionary.AddRange(items);

                    // then
                    monitoredObservableDictionary
                        .Should().Raise(nameof(observableDictionary.PropertyChanged))
                        .WithSender(observableDictionary)
                        .WithArgs<PropertyChangedEventArgs>(args => args.PropertyName == "Item[]");

                    monitoredObservableDictionary
                        .Should().Raise(nameof(observableDictionary.PropertyChanged))
                        .WithSender(observableDictionary)
                        .WithArgs<PropertyChangedEventArgs>(args => args.PropertyName == nameof(observableDictionary.Count));
                }
            }
        }

        [Fact(Skip = "Count is currently not captured / filtered properly by FluentAssertions")]
        public void RemoveRangeRaisesPropertyChangedEventForItemIndexerAndCountOnlyOnceWhenThresholdAmountWhenItemChangesAreNotifiedAsResetIsSetAccordingly()
        {
            // given
            var items = new List<KeyValuePair<int, string>>()
                {
                    new KeyValuePair<int, string>(1, "One"),
                    new KeyValuePair<int, string>(2, "Two"),
                    new KeyValuePair<int, string>(3, "Three"),
                };

            using (var observableDictionary = new ObservableDictionary<int, string>(items))
            {
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = 0;
                using (var monitoredObservableDictionary = observableDictionary.Monitor())
                {
                    // when
                    observableDictionary.RemoveRange(items);

                    // then
                    monitoredObservableDictionary
                        .Should().Raise(nameof(observableDictionary.PropertyChanged))
                        .WithSender(observableDictionary)
                        .WithArgs<PropertyChangedEventArgs>(args => args.PropertyName == "Item[]")
                        .Should().HaveCount(1); // ToDo: re-enable test if/when https://github.com/fluentassertions/fluentassertions/issues/337 has been fixed

                    monitoredObservableDictionary
                        .Should().Raise(nameof(observableDictionary.PropertyChanged))
                        .WithSender(observableDictionary)
                        .WithArgs<PropertyChangedEventArgs>(args => args.PropertyName == nameof(observableDictionary.Count))
                        .Should().HaveCount(1); // ToDo: re-enable test if/when https://github.com/fluentassertions/fluentassertions/issues/337 has been fixed
                }
            }
        }

        [Fact]
        public void RemoveRangeRaisesPropertyChangedEventForItemIndexerAndCount()
        {
            // given
            var items = new List<KeyValuePair<int, string>>()
                {
                    new KeyValuePair<int, string>(1, "One"),
                    new KeyValuePair<int, string>(2, "Two"),
                    new KeyValuePair<int, string>(3, "Three"),
                };

            using (var observableDictionary = new ObservableDictionary<int, string>(items))
            {
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = 0;
                using (var monitoredObservableDictionary = observableDictionary.Monitor())
                {
                    // when
                    observableDictionary.RemoveRange(items);

                    // then
                    monitoredObservableDictionary
                        .Should().Raise(nameof(observableDictionary.PropertyChanged))
                        .WithSender(observableDictionary)
                        .WithArgs<PropertyChangedEventArgs>(args => args.PropertyName == "Item[]");

                    monitoredObservableDictionary
                        .Should().Raise(nameof(observableDictionary.PropertyChanged))
                        .WithSender(observableDictionary)
                        .WithArgs<PropertyChangedEventArgs>(args => args.PropertyName == nameof(observableDictionary.Count));
                }
            }
        }

        [Fact]
        public void RemoveRaisesPropertyChangedEventForItemIndexerAndCount()
        {
            // given
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                observableDictionary.Add(1, "One");

                using (var monitoredObservableDictionary = observableDictionary.Monitor())
                {
                    // when
                    observableDictionary.Remove(1);

                    // then
                    monitoredObservableDictionary
                        .Should().Raise(nameof(observableDictionary.PropertyChanged))
                        .WithSender(observableDictionary)
                        .WithArgs<PropertyChangedEventArgs>(args => args.PropertyName == "Item[]");

                    monitoredObservableDictionary
                        .Should().Raise(nameof(observableDictionary.PropertyChanged))
                        .WithSender(observableDictionary)
                        .WithArgs<PropertyChangedEventArgs>(args => args.PropertyName == nameof(observableDictionary.Count));
                }
            }
        }

        [Fact]
        public void ClearNotifiesAsReset()
        {
            // given
            var initialList = new List<KeyValuePair<int, string>>()
            {
                new KeyValuePair<int, string>(1, "Some Value"),
                new KeyValuePair<int, string>(2, "Some Other Value"),
                new KeyValuePair<int, string>(3, "Some Totally Different Value"),
            };

            var scheduler = new TestScheduler();

            var observer = scheduler.CreateObserver<IObservableDictionaryChange<int, string>>();
            var resetsObserver = scheduler.CreateObserver<Unit>();
            var observableCollectionChangesObserver = scheduler.CreateObserver<IObservableCollectionChange<KeyValuePair<int, string>>>();

            using (var observableDictionary = new ObservableDictionary<int, string>(initialList, scheduler: scheduler))
            {
                // when
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = int.MaxValue;

                IDisposable dictionaryChangesSubscription = null;
                IDisposable resetsSubscription = null;
                IDisposable observableCollectionChangesSubscription = null;

                try
                {
                    dictionaryChangesSubscription = observableDictionary.DictionaryChanges.Subscribe(observer);
                    resetsSubscription = observableDictionary.Resets.Subscribe(resetsObserver);
                    observableCollectionChangesSubscription =
                        ((INotifyObservableCollectionChanges<KeyValuePair<int, string>>)observableDictionary)
                        .CollectionChanges
                        .Subscribe(observableCollectionChangesObserver);

                    observableDictionary.Clear();
                    scheduler.AdvanceBy(3);

                    // then
                    observableDictionary.Count.Should().Be(0);

                    resetsObserver.Messages.Count.Should().Be(1);
                    observer.Messages.Count.Should().Be(1);
                    observableCollectionChangesObserver.Messages.Count.Should().Be(1); 

                    observer.Messages.First().Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.Reset);
                    observer.Messages.First().Value.Value.Key.Should().Be(default(int));
                    observer.Messages.First().Value.Value.Value.Should().Be(default(string));
                    observer.Messages.First().Value.Value.OldValue.Should().Be(default(string));
                    observer.Messages.First().Value.Value.ChangedPropertyName.Should().BeEmpty();

                    observableCollectionChangesObserver.Messages.First().Value.Value.ChangeType.Should().Be(ObservableCollectionChangeType.Reset);
                    observableCollectionChangesObserver.Messages.First().Value.Value.Item.Key.Should().Be(default(int));
                    observableCollectionChangesObserver.Messages.First().Value.Value.Item.Value.Should().Be(default(string));
                }
                finally
                {
                    dictionaryChangesSubscription?.Dispose();
                    resetsSubscription?.Dispose();
                    observableCollectionChangesSubscription?.Dispose();
                }
            }
        }

        [Fact]
        public void IsTrackingChangesReturnsCorrectValue()
        {
            // given
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                // when
                using (observableDictionary.SuppressChangeNotifications())
                {
                    observableDictionary.IsTrackingChanges.Should().BeFalse();
                }

                observableDictionary.IsTrackingChanges.Should().BeTrue();
            }
        }

        [Fact]
        public void IsTrackingCountChangesReturnsCorrectValue()
        {
            // given
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                // when
                using (observableDictionary.SuppressCountChangeNotifications())
                {
                    observableDictionary.IsTrackingCountChanges.Should().BeFalse();
                }

                observableDictionary.IsTrackingCountChanges.Should().BeTrue();
            }
        }

        [Fact]
        public void IsTrackingItemChangesReturnsCorrectValue()
        {
            // given
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                // when
                using (observableDictionary.SuppressItemChangeNotifications())
                {
                    observableDictionary.IsTrackingItemChanges.Should().BeFalse();
                }

                observableDictionary.IsTrackingItemChanges.Should().BeTrue();
            }
        }

        [Fact]
        public void IsTrackingResetsReturnsCorrectValue()
        {
            // given
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                // when
                using (observableDictionary.SuppressResetNotifications())
                {
                    observableDictionary.IsTrackingResets.Should().BeFalse();
                }

                observableDictionary.IsTrackingResets.Should().BeTrue();
            }
        }

        [Fact]
        public void ShouldNotNotifySubscribersAboutKeyChangesAfterItemsAreRemovedFromDictionary()
        {
            // given
            var scheduler = new TestScheduler();

            int value = 1;
            var key = new MyNotifyPropertyChanged<int, string>(value);

            var observer = scheduler.CreateObserver<IObservableDictionaryChange<MyNotifyPropertyChanged<int, string>, int>>();
            var itemChangesObserver = scheduler.CreateObserver<IObservableDictionaryChange<MyNotifyPropertyChanged<int, string>, int>>();

            using (var observableDictionary = new ObservableDictionary<MyNotifyPropertyChanged<int, string>, int>(scheduler: scheduler))
            {
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = int.MaxValue;

                IDisposable dictionaryChangesSubscription = null;
                IDisposable dictionaryItemChangesSubscription = null;

                try
                {
                    dictionaryChangesSubscription = observableDictionary.DictionaryChanges.Subscribe(observer);
                    dictionaryItemChangesSubscription = observableDictionary.KeyChanges.Subscribe(itemChangesObserver);

                    // when
                    observableDictionary.Add(key, value); // first general message - ItemAdd
                    scheduler.AdvanceBy(1);

                    key.FirstProperty = Guid.NewGuid().ToString(); // second general / first item change message - ItemChanged
                    scheduler.AdvanceBy(1);

                    observableDictionary.Remove(key); // third general message - ItemRemoved
                    scheduler.AdvanceBy(1);

                    key.SecondProperty = Guid.NewGuid().ToString(); // should no longer be observable on/via dictionary
                    scheduler.AdvanceBy(1);

                    //scheduler.AdvanceBy(100);

                    // then
                    observer.Messages.Count.Should().Be(3);
                    observer.Messages[0].Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.ItemAdded);
                    observer.Messages[0].Value.Value.Key.Should().Be(key);
                    observer.Messages[0].Value.Value.Value.Should().Be(value);

                    observer.Messages[1].Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.ItemKeyChanged);
                    observer.Messages[1].Value.Value.Key.Should().Be(key);
                    observer.Messages[1].Value.Value.Value.Should().Be(default(int));
                    observer.Messages[1].Value.Value.OldValue.Should().Be(default(int));
                    observer.Messages[1].Value.Value.ChangedPropertyName.Should().Be(nameof(MyNotifyPropertyChanged<int, string>.FirstProperty));

                    observer.Messages[2].Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.ItemRemoved);
                    observer.Messages[2].Value.Value.Key.Should().Be(key);
                    observer.Messages[2].Value.Value.Value.Should().Be(value);

                    itemChangesObserver.Messages.Count.Should().Be(1);
                    itemChangesObserver.Messages.First().Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.ItemKeyChanged);
                    itemChangesObserver.Messages.First().Value.Value.Key.Should().Be(key);
                    itemChangesObserver.Messages.First().Value.Value.Value.Should().Be(default(int));
                    itemChangesObserver.Messages.First().Value.Value.OldValue.Should().Be(default(int));
                    itemChangesObserver.Messages.First().Value.Value.ChangedPropertyName.Should().Be(nameof(MyNotifyPropertyChanged<int, string>.FirstProperty));
                }
                finally
                {
                    dictionaryChangesSubscription?.Dispose();
                    dictionaryItemChangesSubscription?.Dispose();
                }
            }
        }

        [Fact]
        public void ShouldNotNotifySubscribersAboutValueChangesAfterItemsAreRemovedFromDictionary()
        {
            // given
            var scheduler = new TestScheduler();

            int key = 1;
            var testInpcImplementationInstance = new MyNotifyPropertyChanged<int, string>(key);

            var observer = scheduler.CreateObserver<IObservableDictionaryChange<int, MyNotifyPropertyChanged<int, string>>>();
            var itemChangesObserver = scheduler.CreateObserver<IObservableDictionaryChange<int, MyNotifyPropertyChanged<int, string>>>();

            using (var observableDictionary = new ObservableDictionary<int, MyNotifyPropertyChanged<int, string>>(scheduler: scheduler))
            {
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = int.MaxValue;

                IDisposable dictionaryChangesSubscription = null;
                IDisposable dictionaryItemChangesSubscription = null;

                try
                {
                    dictionaryChangesSubscription = observableDictionary.DictionaryChanges.Subscribe(observer);
                    dictionaryItemChangesSubscription = observableDictionary.ValueChanges.Subscribe(itemChangesObserver);

                    // when
                    observableDictionary.Add(key, testInpcImplementationInstance); // first general message - ItemAdd
                    testInpcImplementationInstance.FirstProperty = Guid.NewGuid().ToString(); // second general / first item change message - ItemChanged
                    observableDictionary.Remove(key); // third general message - ItemRemoved
                    testInpcImplementationInstance.SecondProperty = Guid.NewGuid().ToString(); // should no longer be observable on/via dictionary

                    scheduler.AdvanceBy(100);

                    // then
                    observer.Messages.Count.Should().Be(3);
                    observer.Messages[0].Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.ItemAdded);
                    observer.Messages[0].Value.Value.Key.Should().Be(key);
                    observer.Messages[0].Value.Value.Value.Should().Be(testInpcImplementationInstance);

                    observer.Messages[1].Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.ItemValueChanged);
                    observer.Messages[1].Value.Value.Key.Should().Be(default(int));
                    observer.Messages[1].Value.Value.Value.Should().Be(testInpcImplementationInstance);
                    observer.Messages[1].Value.Value.OldValue.Should().BeNull();
                    observer.Messages[1].Value.Value.ChangedPropertyName.Should().Be(nameof(MyNotifyPropertyChanged<int, string>.FirstProperty));

                    observer.Messages[2].Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.ItemRemoved);
                    observer.Messages[2].Value.Value.Key.Should().Be(key);
                    observer.Messages[2].Value.Value.Value.Should().Be(testInpcImplementationInstance);

                    itemChangesObserver.Messages.Count.Should().Be(1);
                    itemChangesObserver.Messages.First().Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.ItemValueChanged);
                    itemChangesObserver.Messages.First().Value.Value.Key.Should().Be(default(int));
                    itemChangesObserver.Messages.First().Value.Value.Value.Should().Be(testInpcImplementationInstance);
                    itemChangesObserver.Messages.First().Value.Value.OldValue.Should().BeNull();
                    itemChangesObserver.Messages.First().Value.Value.ChangedPropertyName.Should().Be(nameof(MyNotifyPropertyChanged<int, string>.FirstProperty));
                }
                finally
                {
                    dictionaryChangesSubscription?.Dispose();
                    dictionaryItemChangesSubscription?.Dispose();
                }
            }
        }

        [Fact]
        public void ShouldNotifySubscribersAboutKeyChangesWhileItemsAreInDictionary()
        {
            // given
            var scheduler = new TestScheduler();

            int value = 1;
            var key = new MyNotifyPropertyChanged<int, string>(value);

            var observer = scheduler.CreateObserver<IObservableDictionaryChange<MyNotifyPropertyChanged<int, string>, int>>();
            var itemChangesObserver = scheduler.CreateObserver<IObservableDictionaryChange<MyNotifyPropertyChanged<int, string>, int>>();
            var collectionItemChangesObserver = scheduler.CreateObserver<IObservableCollectionChange<KeyValuePair<MyNotifyPropertyChanged<int, string>, int>>>();

            using (var observableDictionary = new ObservableDictionary<MyNotifyPropertyChanged<int, string>, int>(scheduler: scheduler))
            {
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = int.MaxValue;

                IDisposable dictionaryChangesSubscription = null;
                IDisposable dictionaryKeyChangesSubscription = null;
                IDisposable observableCollectionItemChangesSubscription = null;

                try
                {
                    dictionaryChangesSubscription = observableDictionary.DictionaryChanges.Subscribe(observer);
                    dictionaryKeyChangesSubscription = observableDictionary.KeyChanges.Subscribe(itemChangesObserver);
                    observableCollectionItemChangesSubscription = ((INotifyObservableCollectionItemChanges<KeyValuePair<MyNotifyPropertyChanged<int, string>, int>>)observableDictionary)
                        .CollectionItemChanges
                        .Subscribe(collectionItemChangesObserver);

                    // when
                    observableDictionary.Add(key, value);

                    key.FirstProperty = Guid.NewGuid().ToString();
                    scheduler.AdvanceBy(100);

                    // then
                    observer.Messages.Count.Should().Be(2);
                    itemChangesObserver.Messages.Count.Should().Be(1);
                    collectionItemChangesObserver.Messages.Count.Should().Be(1);

                    observer.Messages.First().Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.ItemAdded);
                    observer.Messages.First().Value.Value.Key.Should().Be(key);
                    observer.Messages.First().Value.Value.Value.Should().Be(value);

                    observer.Messages.Last().Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.ItemKeyChanged);
                    observer.Messages.Last().Value.Value.Key.Should().Be(key);
                    observer.Messages.Last().Value.Value.Value.Should().Be(default(int));
                    observer.Messages.Last().Value.Value.OldValue.Should().Be(default(int));
                    observer.Messages.Last().Value.Value.ChangedPropertyName.Should().Be(nameof(MyNotifyPropertyChanged<int, string>.FirstProperty));

                    itemChangesObserver.Messages.First().Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.ItemKeyChanged);
                    itemChangesObserver.Messages.First().Value.Value.Key.Should().Be(key);
                    itemChangesObserver.Messages.First().Value.Value.Value.Should().Be(default(int));
                    itemChangesObserver.Messages.First().Value.Value.OldValue.Should().Be(default(int));
                    itemChangesObserver.Messages.Last().Value.Value.ChangedPropertyName.Should().Be(nameof(MyNotifyPropertyChanged<int, string>.FirstProperty));

                    collectionItemChangesObserver.Messages.First().Value.Value.ChangeType.Should().Be(ObservableCollectionChangeType.ItemChanged);
                    collectionItemChangesObserver.Messages.First().Value.Value.Item.Key.Should().Be(key);
                    collectionItemChangesObserver.Messages.First().Value.Value.Item.Value.Should().Be(value);
                }
                finally
                {
                    dictionaryChangesSubscription?.Dispose();
                    dictionaryKeyChangesSubscription?.Dispose();
                    observableCollectionItemChangesSubscription?.Dispose();
                }
            }
        }

        [Fact]
        public void ShouldNotifySubscribersAboutValueChangesWhileItemsAreInDictionary()
        {
            // given
            var scheduler = new TestScheduler();

            int key = 1;
            var testInpcImplementationInstance = new MyNotifyPropertyChanged<int, string>(key);

            var observer = scheduler.CreateObserver<IObservableDictionaryChange<int, MyNotifyPropertyChanged<int, string>>>();
            var itemChangesObserver = scheduler.CreateObserver<IObservableDictionaryChange<int, MyNotifyPropertyChanged<int, string>>>();
            var collectionItemChangesObserver = scheduler.CreateObserver<IObservableCollectionChange<KeyValuePair<int, MyNotifyPropertyChanged<int, string>>>>();

            using (var observableDictionary = new ObservableDictionary<int, MyNotifyPropertyChanged<int, string>>(scheduler: scheduler))
            {
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = int.MaxValue;

                IDisposable dictionaryChangesSubscription = null;
                IDisposable dictionaryItemChangesSubscription = null;
                IDisposable observableCollectionItemChangesSubscription = null;

                try
                {
                    dictionaryChangesSubscription = observableDictionary.DictionaryChanges.Subscribe(observer);
                    dictionaryItemChangesSubscription = observableDictionary.ValueChanges.Subscribe(itemChangesObserver);
                    observableCollectionItemChangesSubscription = ((INotifyObservableCollectionItemChanges<KeyValuePair<int, MyNotifyPropertyChanged<int, string>>>)observableDictionary)
                        .CollectionItemChanges
                        .Subscribe(collectionItemChangesObserver);

                    // when
                    observableDictionary.Add(key, testInpcImplementationInstance);

                    testInpcImplementationInstance.FirstProperty = Guid.NewGuid().ToString();
                    scheduler.AdvanceBy(100);

                    // then
                    observer.Messages.Count.Should().Be(2);
                    itemChangesObserver.Messages.Count.Should().Be(1);
                    collectionItemChangesObserver.Messages.Count.Should().Be(1);

                    observer.Messages.First().Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.ItemAdded);
                    observer.Messages.First().Value.Value.Key.Should().Be(key);
                    observer.Messages.First().Value.Value.Value.Should().Be(testInpcImplementationInstance);

                    observer.Messages.Last().Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.ItemValueChanged);
                    observer.Messages.Last().Value.Value.Key.Should().Be(default(int));
                    observer.Messages.Last().Value.Value.Value.Should().Be(testInpcImplementationInstance);
                    observer.Messages.Last().Value.Value.OldValue.Should().BeNull();
                    observer.Messages.Last().Value.Value.ChangedPropertyName.Should().Be(nameof(MyNotifyPropertyChanged<int, string>.FirstProperty));

                    itemChangesObserver.Messages.First().Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.ItemValueChanged);
                    itemChangesObserver.Messages.First().Value.Value.Key.Should().Be(default(int));
                    itemChangesObserver.Messages.First().Value.Value.Value.Should().Be(testInpcImplementationInstance);
                    itemChangesObserver.Messages.First().Value.Value.OldValue.Should().BeNull();
                    itemChangesObserver.Messages.Last().Value.Value.ChangedPropertyName.Should().Be(nameof(MyNotifyPropertyChanged<int, string>.FirstProperty));

                    collectionItemChangesObserver.Messages.First().Value.Value.ChangeType.Should().Be(ObservableCollectionChangeType.ItemChanged);
                    collectionItemChangesObserver.Messages.First().Value.Value.Item.Key.Should().Be(key);
                    collectionItemChangesObserver.Messages.First().Value.Value.Item.Value.Should().Be(testInpcImplementationInstance);
                }
                finally
                {
                    dictionaryChangesSubscription?.Dispose();
                    dictionaryItemChangesSubscription?.Dispose();
                    observableCollectionItemChangesSubscription?.Dispose();
                }
            }
        }

        [Fact]
        public void ShouldNotifySubscribersAboutKeyChangesAsResetIfRequestedWhileItemsAreInDictionary()
        {
            // given
            var scheduler = new TestScheduler();

            int value = 1;
            var key = new MyNotifyPropertyChanged<int, string>(value);

            var observer = scheduler.CreateObserver<IObservableDictionaryChange<MyNotifyPropertyChanged<int, string>, int>>();
            var itemChangesObserver = scheduler.CreateObserver<IObservableDictionaryChange<MyNotifyPropertyChanged<int, string>, int>>();
            var resetsObserver = scheduler.CreateObserver<Unit>();

            using (var observableDictionary = new ObservableDictionary<MyNotifyPropertyChanged<int, string>, int>(scheduler: scheduler))
            {
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = Int32.MaxValue;

                IDisposable dictionaryChangesSubscription = null;
                IDisposable dictionaryItemChangesSubscription = null;
                IDisposable dictionaryResetsSubscription = null;

                try
                {
                    dictionaryChangesSubscription = observableDictionary.DictionaryChanges.Subscribe(observer);
                    dictionaryItemChangesSubscription = observableDictionary.KeyChanges.Subscribe(itemChangesObserver);
                    dictionaryResetsSubscription = observableDictionary.Resets.Subscribe(resetsObserver);

                    // when
                    observableDictionary.Add(key, value);

                    observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = 0;
                    key.FirstProperty = Guid.NewGuid().ToString();
                    scheduler.AdvanceBy(100);

                    // then
                    observer.Messages.Count.Should().Be(2);
                    itemChangesObserver.Messages.Count.Should().Be(0);
                    resetsObserver.Messages.Count.Should().Be(1);

                    observer.Messages.First().Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.ItemAdded);
                    observer.Messages.First().Value.Value.Key.Should().Be(key);
                    observer.Messages.First().Value.Value.Value.Should().Be(value);

                    observer.Messages.Last().Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.Reset);
                    observer.Messages.Last().Value.Value.Key.Should().Be(default(MyNotifyPropertyChanged<int, string>));
                    observer.Messages.Last().Value.Value.Value.Should().Be(default(int));
                    observer.Messages.Last().Value.Value.OldValue.Should().Be(default(int));
                    observer.Messages.Last().Value.Value.ChangedPropertyName.Should().BeEmpty();
                }
                finally
                {
                    dictionaryChangesSubscription?.Dispose();
                    dictionaryItemChangesSubscription?.Dispose();
                    dictionaryResetsSubscription?.Dispose();
                }
            }
        }

        [Fact]
        public void ShouldNotifySubscribersAboutValueChangesAsResetIfRequestedWhileItemsAreInDictionary()
        {
            // given
            var scheduler = new TestScheduler();

            int key = 1;
            var testInpcImplementationInstance = new MyNotifyPropertyChanged<int, string>(key);

            var observer = scheduler.CreateObserver<IObservableDictionaryChange<int, MyNotifyPropertyChanged<int, string>>>();
            var itemChangesObserver = scheduler.CreateObserver<IObservableDictionaryChange<int, MyNotifyPropertyChanged<int, string>>>();
            var resetsObserver = scheduler.CreateObserver<Unit>();

            using (var observableDictionary = new ObservableDictionary<int, MyNotifyPropertyChanged<int, string>>(scheduler: scheduler))
            {
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = Int32.MaxValue;

                IDisposable dictionaryChangesSubscription = null;
                IDisposable dictionaryItemChangesSubscription = null;
                IDisposable dictionaryResetsSubscription = null;

                try
                {
                    dictionaryChangesSubscription = observableDictionary.DictionaryChanges.Subscribe(observer);
                    dictionaryItemChangesSubscription = observableDictionary.ValueChanges.Subscribe(itemChangesObserver);
                    dictionaryResetsSubscription = observableDictionary.Resets.Subscribe(resetsObserver);

                    // when
                    observableDictionary.Add(key, testInpcImplementationInstance);

                    observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = 0;
                    testInpcImplementationInstance.FirstProperty = Guid.NewGuid().ToString();
                    scheduler.AdvanceBy(100);

                    // then
                    observer.Messages.Count.Should().Be(2);
                    itemChangesObserver.Messages.Count.Should().Be(0);
                    resetsObserver.Messages.Count.Should().Be(1);

                    observer.Messages.First().Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.ItemAdded);
                    observer.Messages.First().Value.Value.Key.Should().Be(key);
                    observer.Messages.First().Value.Value.Value.Should().Be(testInpcImplementationInstance);

                    observer.Messages.Last().Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.Reset);
                    observer.Messages.Last().Value.Value.Key.Should().Be(default(int));
                    observer.Messages.Last().Value.Value.Value.Should().Be(default(MyNotifyPropertyChanged<int, string>));
                    observer.Messages.Last().Value.Value.OldValue.Should().Be(default(MyNotifyPropertyChanged<int, string>));
                    observer.Messages.Last().Value.Value.ChangedPropertyName.Should().BeEmpty();
                }
                finally
                {
                    dictionaryChangesSubscription?.Dispose();
                    dictionaryItemChangesSubscription?.Dispose();
                    dictionaryResetsSubscription?.Dispose();
                }
            }
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(100, 10)]
        [InlineData(1, 0)]
        public void RemoveNotifiesCountDecrease(int initialDictionarySize, int amountOfItemsToRemove)
        {
            if (amountOfItemsToRemove > initialDictionarySize)
                throw new ArgumentOutOfRangeException(nameof(amountOfItemsToRemove), $"Must be less than {nameof(initialDictionarySize)}");

            // given
            var initialValues = Enumerable.Range(0, initialDictionarySize).ToDictionary(item => item, item => $"#{item}");

            int observableReportedCount = initialValues.Count;
            int countChangesCalled = 0;

            using (var observableDictionary = new ObservableDictionary<int, string>(initialValues))
            {
                // when
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = int.MaxValue;
                observableDictionary.CountChanges.Subscribe(i =>
                {
                    observableReportedCount = i;
                    countChangesCalled++;
                });

                for (int i = 0; i < amountOfItemsToRemove; i++)
                {
                    observableDictionary.Remove(observableDictionary.Last().Key);
                }

                // then check whether all items have been accounted for
                var expectedCount = initialDictionarySize - amountOfItemsToRemove;

                observableReportedCount.Should().Be(expectedCount); // +1 because the upper for loop goes up to & inclusive the upperLimit
                observableReportedCount.Should().Be(observableDictionary.Count);

                countChangesCalled.Should().Be(amountOfItemsToRemove);
            }
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(10, 5)]
        [InlineData(100, 99)]
        public void RemoveNotifiesRemoval(int initialDictionarySize, int amountOfItemsToRemove)
        {
            if (amountOfItemsToRemove > initialDictionarySize)
                throw new ArgumentOutOfRangeException(nameof(amountOfItemsToRemove), $"Must be less than {nameof(initialDictionarySize)}");

            // given
            var scheduler = new TestScheduler();

            var initialValues = Enumerable.Range(0, initialDictionarySize).ToDictionary(item => item, item => $"#{item}");
            var observer = scheduler.CreateObserver<IObservableDictionaryChange<int, string>>();

            using (var observableDictionary = new ObservableDictionary<int, string>(initialValues, scheduler: scheduler))
            {
                // when
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = int.MaxValue;

                using (observableDictionary.DictionaryChanges.Subscribe(observer))
                {
                    var removedKeyValuePairs = new List<KeyValuePair<int, string>>();

                    for (int i = 0; i < amountOfItemsToRemove; i++)
                    {
                        var lastEntry = observableDictionary.Last();
                        observableDictionary.Remove(lastEntry.Key);

                        removedKeyValuePairs.Add(lastEntry);

                        scheduler.AdvanceBy(1);
                    }

                    // then
                    observableDictionary.Count.Should().Be(initialDictionarySize - amountOfItemsToRemove);
                    observer.Messages.Count.Should().Be(amountOfItemsToRemove);

                    if (initialDictionarySize > 0)
                    {
                        observer.Messages.Select(message => message.Value.Value.ChangeType).Should().OnlyContain(changeType => changeType == ObservableDictionaryChangeType.ItemRemoved);

                        observer.Messages.Select(message => message.Value.Value.Key).Should().Contain(removedKeyValuePairs.Select(kvp => kvp.Key));
                        observer.Messages.Select(message => message.Value.Value.Value).Should().Contain(removedKeyValuePairs.Select(kvp => kvp.Value));
                    }
                }
            }
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(10, 5)]
        [InlineData(100, 99)]
        public void RemoveNotifiesRemovalAsResetIfRequestedTest(int initialDictionarySize, int amountOfItemsToRemove)
        {
            if (amountOfItemsToRemove > initialDictionarySize)
                throw new ArgumentOutOfRangeException(nameof(amountOfItemsToRemove), $"Must be less than {nameof(initialDictionarySize)}");

            // given
            var scheduler = new TestScheduler();

            var initialValues = Enumerable.Range(0, initialDictionarySize).ToDictionary(item => item, item => $"#{item}");
            var observer = scheduler.CreateObserver<IObservableDictionaryChange<int, string>>();
            var resetsObserver = scheduler.CreateObserver<Unit>();

            using (var observableDictionary = new ObservableDictionary<int, string>(initialValues, scheduler: scheduler))
            {
                // when
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = 0;

                using (observableDictionary.DictionaryChanges.Subscribe(observer))
                {
                    using (observableDictionary.Resets.Subscribe(resetsObserver))
                    {
                        var removedKeyValuePairs = new List<KeyValuePair<int, string>>();

                        for (int i = 0; i < amountOfItemsToRemove; i++)
                        {
                            var lastEntry = observableDictionary.Last();
                            observableDictionary.Remove(lastEntry.Key);

                            removedKeyValuePairs.Add(lastEntry);

                            scheduler.AdvanceBy(2);
                        }

                        // then
                        observableDictionary.Count.Should().Be(initialDictionarySize - amountOfItemsToRemove);

                        observer.Messages.Count.Should().Be(amountOfItemsToRemove);
                        resetsObserver.Messages.Count.Should().Be(amountOfItemsToRemove);

                        if (initialDictionarySize > 0)
                        {
                            observer.Messages.Select(message => message.Value.Value.ChangeType).Should().OnlyContain(changeType => changeType == ObservableDictionaryChangeType.Reset);

                            observer.Messages.Select(message => message.Value.Value.Key).Should().Match(ints => ints.All(@int => Equals(default(int), @int)));
                            observer.Messages.Select(message => message.Value.Value.Value).Should().Match(strings => strings.All(@string => Equals(default(string), @string)));
                        }
                    }
                }
            }
        }

        [Fact]
        public void ResetNotifiesAsReset()
        {
            // given
            var scheduler = new TestScheduler();
            var observer = scheduler.CreateObserver<IObservableDictionaryChange<int, string>>();
            var resetsObserver = scheduler.CreateObserver<Unit>();
            var observableCollectionChangesObserver = scheduler.CreateObserver<IObservableCollectionChange<KeyValuePair<int, string>>>();

            using (var observableDictionary = new ObservableDictionary<int, string>(scheduler: scheduler))
            {
                // when
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = int.MaxValue;

                IDisposable dictionaryChangesSubscription = null;
                IDisposable resetsSubscription = null;
                IDisposable observableCollectionChangesSubscription = null;

                try
                {
                    dictionaryChangesSubscription = observableDictionary.DictionaryChanges.Subscribe(observer);
                    resetsSubscription = observableDictionary.Resets.Subscribe(resetsObserver);
                    observableCollectionChangesSubscription =
                        ((INotifyObservableCollectionChanges<KeyValuePair<int, string>>)observableDictionary)
                        .CollectionChanges
                        .Subscribe(observableCollectionChangesObserver);

                    observableDictionary.Reset();
                    scheduler.AdvanceBy(3);

                    // then
                    resetsObserver.Messages.Count.Should().Be(1);
                    observer.Messages.Count.Should().Be(1);
                    observableCollectionChangesObserver.Messages.Count.Should().Be(1);

                    observer.Messages.First().Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.Reset);
                    observer.Messages.First().Value.Value.Key.Should().Be(default(int));
                    observer.Messages.First().Value.Value.Value.Should().Be(default(string));
                    observer.Messages.First().Value.Value.OldValue.Should().Be(default(string));
                    observer.Messages.First().Value.Value.ChangedPropertyName.Should().BeEmpty();

                    observableCollectionChangesObserver.Messages.First().Value.Value.ChangeType.Should().Be(ObservableCollectionChangeType.Reset);
                    observableCollectionChangesObserver.Messages.First().Value.Value.Item.Key.Should().Be(default(int));
                    observableCollectionChangesObserver.Messages.First().Value.Value.Item.Value.Should().Be(default(string));
                }
                finally
                {
                    dictionaryChangesSubscription?.Dispose();
                    resetsSubscription?.Dispose();
                    observableCollectionChangesSubscription?.Dispose();
                }
            }
        }

        [Fact]
        public void SuppressChangeNotificationsDisposalShouldThrowObjectDisposedExceptionAfterDictionaryDisposal()
        {
            // given
            var observableDictionary = new ObservableDictionary<int, string>();
            var suppression = observableDictionary.SuppressChangeNotifications();
            observableDictionary.Dispose();

            // when
            Action action = () => { suppression.Dispose(); };

            action
                .Should().Throw<ObjectDisposedException>()
                .WithMessage($"Cannot access a disposed object.\r\nObject name: '{observableDictionary.GetType().Name}'.");
        }

        [Fact]
        public void SuppressChangeNotificationsPreventsMultipleSuppressions()
        {
            // given
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                // when
                using (observableDictionary.SuppressChangeNotifications())
                {
                    Action action = () => { var secondSuppression = observableDictionary.SuppressChangeNotifications(); };

                    action
                        .Should().Throw<InvalidOperationException>()
                        .WithMessage("A Change Notification Suppression is currently already ongoing, multiple concurrent suppressions are not supported.");
                }
            }
        }

        [Fact]
        public void SuppressChangeNotificationsShouldThrowObjectDisposedExceptionAfterDictionaryDisposal()
        {
            // given
            var observableDictionary = new ObservableDictionary<int, string>();
            observableDictionary.Dispose();

            // when
            Action action = () => { var suppression = observableDictionary.SuppressChangeNotifications(); };

            action
                .Should().Throw<ObjectDisposedException>()
                .WithMessage($"Cannot access a disposed object.\r\nObject name: '{observableDictionary.GetType().Name}'.");
        }

        [Fact]
        public void SuppressCountChangedNotificationsDisposalShouldThrowObjectDisposedExceptionAfterDictionaryDisposal()
        {
            // given
            var observableDictionary = new ObservableDictionary<int, string>();
            var suppression = observableDictionary.SuppressCountChangeNotifications();
            observableDictionary.Dispose();

            // when
            Action action = () => { suppression.Dispose(); };

            action
                .Should().Throw<ObjectDisposedException>()
                .WithMessage($"Cannot access a disposed object.\r\nObject name: '{observableDictionary.GetType().Name}'.");
        }

        [Fact]
        public void SuppressCountChangedNotificationsNotifiesCountWhenSuppressionIsDisposedIfRequested()
        {
            // given
            var scheduler = new TestScheduler();

            var countChangesObserver = scheduler.CreateObserver<int>();
            var observer = scheduler.CreateObserver<IObservableDictionaryChange<int, string>>();

            using (var observableDictionary = new ObservableDictionary<int, string>(scheduler: scheduler))
            {
                // when
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = int.MaxValue;

                IDisposable countChangedSubscription = null;
                IDisposable dictionaryChangesSubscription = null;

                try
                {
                    countChangedSubscription = observableDictionary.CountChanges.Subscribe(countChangesObserver);
                    dictionaryChangesSubscription = observableDictionary.DictionaryChanges.Subscribe(observer);

                    using (observableDictionary.SuppressCountChangeNotifications(true))
                    {
                        observableDictionary.Add(1, "One");
                        observableDictionary.Add(2, "Two");
                        observableDictionary.Add(3, "Three");
                        observableDictionary.Remove(3);

                        scheduler.AdvanceBy(8);
                    }

                    scheduler.AdvanceBy(2);

                    // then
                    countChangesObserver.Messages.Count.Should().Be(1);
                    observer.Messages.Count.Should().Be(4);
                }
                finally
                {
                    countChangedSubscription?.Dispose();
                    dictionaryChangesSubscription?.Dispose();
                }
            }
        }

        [Fact]
        public void SuppressCountChangedNotificationsPreventsMultipleSuppressions()
        {
            // given
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                // when
                using (observableDictionary.SuppressCountChangeNotifications())
                {
                    Action action = () => { var secondSuppression = observableDictionary.SuppressCountChangeNotifications(); };

                    action
                        .Should().Throw<InvalidOperationException>()
                        .WithMessage("A Count Change(s) Notification Suppression is currently already ongoing, multiple concurrent suppressions are not supported.");
                }
            }
        }

        [Fact]
        public void SuppressCountChangedNotificationsShouldThrowObjectDisposedExceptionAfterDictionaryDisposal()
        {
            // given
            var observableDictionary = new ObservableDictionary<int, string>();
            observableDictionary.Dispose();

            // when
            Action action = () => { var suppression = observableDictionary.SuppressCountChangeNotifications(); };

            action
                .Should().Throw<ObjectDisposedException>()
                .WithMessage($"Cannot access a disposed object.\r\nObject name: '{observableDictionary.GetType().Name}'.");
        }

        [Fact]
        public void SuppressCountChangedNotificationsSuppressesCountChangedNotifications()
        {
            // given
            var scheduler = new TestScheduler();
            var countChangesObserver = scheduler.CreateObserver<int>();

            using (var observableDictionary = new ObservableDictionary<int, string>(scheduler: scheduler))
            {
                // when
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = int.MaxValue;

                IDisposable countChangesSubscription = null;

                try
                {
                    countChangesSubscription = observableDictionary.CountChanges.Subscribe(countChangesObserver);

                    using (observableDictionary.SuppressCountChangeNotifications(false))
                    {
                        observableDictionary.Add(1, "One");
                        observableDictionary.Add(2, "Two");

                        observableDictionary.Remove(1);
                        observableDictionary.Remove(2);

                        scheduler.AdvanceBy(4);
                    }

                    scheduler.AdvanceBy(1);

                    // then
                    countChangesObserver.Messages.Should().BeEmpty();
                }
                finally
                {
                    countChangesSubscription?.Dispose();
                }
            }
        }

        [Fact]
        public void SuppressItemChangedNotificationsDisposalShouldThrowObjectDisposedExceptionAfterDictionaryDisposal()
        {
            // given
            var observableDictionary = new ObservableDictionary<int, string>();
            var suppression = observableDictionary.SuppressItemChangeNotifications();
            observableDictionary.Dispose();

            // when
            Action action = () => { suppression.Dispose(); };

            action
                .Should().Throw<ObjectDisposedException>()
                .WithMessage($"Cannot access a disposed object.\r\nObject name: '{observableDictionary.GetType().Name}'.");
        }

        [Fact]
        public void SuppressItemChangedNotificationsNotifiesResetWhenSuppressionIsDisposedIfRequested()
        {
            // given
            var scheduler = new TestScheduler();
            var dictionaryChangesObserver = scheduler.CreateObserver<IObservableDictionaryChange<int, string>>();
            var dictionaryItemChangesObserver = scheduler.CreateObserver<IObservableDictionaryChange<int, string>>();
            var resetsObserver = scheduler.CreateObserver<Unit>();

            var initialKvps = new List<KeyValuePair<int, string>>()
            {
                new KeyValuePair<int, string>(1, "One")
            };

            using (var observableDictionary = new ObservableDictionary<int, string>(initialKvps, scheduler: scheduler))
            {
                // when
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = int.MaxValue;

                IDisposable dictionaryChangesSubscription = null;
                IDisposable dictionaryItemChangesSubscription = null;
                IDisposable resetsSubscription = null;

                try
                {
                    dictionaryChangesSubscription = observableDictionary.DictionaryChanges.Subscribe(dictionaryChangesObserver);
                    dictionaryItemChangesSubscription = observableDictionary.ValueChanges.Subscribe(dictionaryItemChangesObserver);
                    resetsSubscription = observableDictionary.Resets.Subscribe(resetsObserver);

                    using (observableDictionary.SuppressItemChangeNotifications(true))
                    {
                        observableDictionary[1] = "Two";

                        scheduler.AdvanceBy(4);
                    }

                    scheduler.AdvanceBy(4);

                    // then
                    resetsObserver.Messages.Count.Should().Be(1);

                    dictionaryItemChangesObserver.Messages.Count.Should().Be(0);
                    dictionaryChangesObserver.Messages.Count.Should().Be(1);

                    dictionaryChangesObserver.Messages.First().Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.Reset);
                    dictionaryChangesObserver.Messages.First().Value.Value.Key.Should().Be(default(int));
                    dictionaryChangesObserver.Messages.First().Value.Value.Value.Should().Be(default(string));
                    dictionaryChangesObserver.Messages.First().Value.Value.OldValue.Should().Be(default(string));
                    dictionaryChangesObserver.Messages.First().Value.Value.ChangedPropertyName.Should().BeEmpty();
                }
                finally
                {
                    dictionaryChangesSubscription?.Dispose();
                    dictionaryItemChangesSubscription?.Dispose();
                    resetsSubscription?.Dispose();
                }
            }
        }

        [Fact]
        public void SuppressItemChangedNotificationsPreventsMultipleSuppressions()
        {
            // given
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                // when
                using (observableDictionary.SuppressItemChangeNotifications())
                {
                    Action action = () => { var secondSuppression = observableDictionary.SuppressItemChangeNotifications(); };

                    action
                        .Should().Throw<InvalidOperationException>()
                        .WithMessage("An Item Change Notification Suppression is currently already ongoing, multiple concurrent suppressions are not supported.");
                }
            }
        }

        [Fact]
        public void SuppressItemChangedNotificationsShouldThrowObjectDisposedExceptionAfterDictionaryDisposal()
        {
            // given
            var observableDictionary = new ObservableDictionary<int, string>();
            observableDictionary.Dispose();

            // when
            Action action = () => { var suppression = observableDictionary.SuppressItemChangeNotifications(); };

            action
                .Should().Throw<ObjectDisposedException>()
                .WithMessage($"Cannot access a disposed object.\r\nObject name: '{observableDictionary.GetType().Name}'.");
        }

        [Fact]
        public void SuppressItemChangedNotificationsSuppressesItemChangedNotifications()
        {
            // given
            var scheduler = new TestScheduler();
            var observer = scheduler.CreateObserver<IObservableDictionaryChange<int, string>>();
            var resetsObserver = scheduler.CreateObserver<Unit>();

            var initialKvps = new List<KeyValuePair<int, string>>()
            {
                new KeyValuePair<int, string>(1, "One")
            };

            using (var observableDictionary = new ObservableDictionary<int, string>(initialKvps, scheduler: scheduler))
            {
                // when
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = int.MaxValue;

                IDisposable dictionaryItemChangesSubscription = null;
                IDisposable resetsSubscription = null;

                try
                {
                    dictionaryItemChangesSubscription = observableDictionary.ValueChanges.Subscribe(observer);
                    resetsSubscription = observableDictionary.Resets.Subscribe(resetsObserver);

                    using (observableDictionary.SuppressItemChangeNotifications(false))
                    {
                        observableDictionary[1] = "Two";

                        scheduler.AdvanceBy(2);
                    }

                    scheduler.AdvanceBy(2);

                    // then
                    observer.Messages.Should().BeEmpty();
                    resetsObserver.Messages.Should().BeEmpty();
                }
                finally
                {
                    dictionaryItemChangesSubscription?.Dispose();
                    resetsSubscription?.Dispose();
                }
            }
        }

        [Fact]
        public void SuppressResetNotificationsDisposalShouldThrowObjectDisposedExceptionAfterDictionaryDisposal()
        {
            // given
            var observableDictionary = new ObservableDictionary<int, string>();
            var suppression = observableDictionary.SuppressResetNotifications();
            observableDictionary.Dispose();

            // when
            Action action = () => { suppression.Dispose(); };

            action
                .Should().Throw<ObjectDisposedException>()
                .WithMessage($"Cannot access a disposed object.\r\nObject name: '{observableDictionary.GetType().Name}'.");
        }

        [Fact]
        public void SuppressResetNotificationsNotifiesResetWhenSuppressionIsDisposedIfRequested()
        {
            // given
            var scheduler = new TestScheduler();
            var observer = scheduler.CreateObserver<IObservableDictionaryChange<int, string>>();
            var resetsObserver = scheduler.CreateObserver<Unit>();

            using (var observableDictionary = new ObservableDictionary<int, string>(scheduler: scheduler))
            {
                // when
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = int.MaxValue;

                IDisposable dictionaryChangesSubscription = null;
                IDisposable resetsSubscription = null;

                try
                {
                    dictionaryChangesSubscription = observableDictionary.DictionaryChanges.Subscribe(observer);
                    resetsSubscription = observableDictionary.Resets.Subscribe(resetsObserver);

                    using (observableDictionary.SuppressResetNotifications(true))
                    {
                        observableDictionary.Reset();
                        observableDictionary.Reset();
                        observableDictionary.Reset();

                        scheduler.AdvanceBy(6);
                    }

                    scheduler.AdvanceBy(2);

                    // then
                    resetsObserver.Messages.Count.Should().Be(1);
                    observer.Messages.Count.Should().Be(1);

                    observer.Messages.First().Value.Value.ChangeType.Should().Be(ObservableDictionaryChangeType.Reset);
                    observer.Messages.First().Value.Value.Key.Should().Be(default(int));
                    observer.Messages.First().Value.Value.Value.Should().Be(default(string));
                    observer.Messages.First().Value.Value.OldValue.Should().Be(default(string));
                    observer.Messages.First().Value.Value.ChangedPropertyName.Should().BeEmpty();
                }
                finally
                {
                    dictionaryChangesSubscription?.Dispose();
                    resetsSubscription?.Dispose();
                }
            }
        }

        [Fact]
        public void SuppressResetNotificationsPreventsMultipleSuppressions()
        {
            // given
            using (var observableDictionary = new ObservableDictionary<int, string>())
            {
                // when
                using (observableDictionary.SuppressResetNotifications())
                {
                    Action action = () => { var secondSuppression = observableDictionary.SuppressResetNotifications(); };

                    action
                        .Should().Throw<InvalidOperationException>()
                        .WithMessage("A Reset(s) Notification Suppression is currently already ongoing, multiple concurrent suppressions are not supported.");
                }
            }
        }

        [Fact]
        public void SuppressResetNotificationsShouldThrowObjectDisposedExceptionAfterDictionaryDisposal()
        {
            // given
            var observableDictionary = new ObservableDictionary<int, string>();
            observableDictionary.Dispose();

            // when
            Action action = () => { var suppression = observableDictionary.SuppressResetNotifications(); };

            action
                .Should().Throw<ObjectDisposedException>()
                .WithMessage($"Cannot access a disposed object.\r\nObject name: '{observableDictionary.GetType().Name}'.");
        }

        [Fact]
        public void SuppressResetNotificationsSuppressesResetNotifications()
        {
            // given
            var scheduler = new TestScheduler();
            var observer = scheduler.CreateObserver<IObservableDictionaryChange<int, string>>();
            var resetsObserver = scheduler.CreateObserver<Unit>();

            using (var observableDictionary = new ObservableDictionary<int, string>(scheduler: scheduler))
            {
                // when
                observableDictionary.ThresholdAmountWhenChangesAreNotifiedAsReset = int.MaxValue;

                IDisposable dictionaryChangesSubscription = null;
                IDisposable resetsSubscription = null;

                try
                {
                    dictionaryChangesSubscription = observableDictionary.DictionaryChanges.Subscribe(observer);
                    resetsSubscription = observableDictionary.Resets.Subscribe(resetsObserver);

                    using (observableDictionary.SuppressResetNotifications(false))
                    {
                        observableDictionary.Reset();

                        scheduler.AdvanceBy(2);
                    }

                    scheduler.AdvanceBy(2);

                    // then
                    observer.Messages.Should().BeEmpty();
                    resetsObserver.Messages.Should().BeEmpty();
                }
                finally
                {
                    dictionaryChangesSubscription?.Dispose();
                    resetsSubscription?.Dispose();
                }
            }
        }
    }
}