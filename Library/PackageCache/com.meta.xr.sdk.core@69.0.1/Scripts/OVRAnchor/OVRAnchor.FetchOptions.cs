/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using static OVRPlugin;
using UnityEngine;

public partial struct OVRAnchor
{
    /// <summary>
    /// Options for <see cref="FetchAnchorsAsync(List{OVRAnchor},FetchOptions,Action{List{OVRAnchor}, int})"/>.
    /// </summary>
    /// <remarks>
    /// When querying for anchors (<see cref="OVRAnchor"/>) using `FetchAnchorsAsync`, you must provide
    /// <see cref="FetchOptions"/> to the query.
    ///
    /// These options filter for the anchors you are interested in. If you provide a default-constructed
    /// <see cref="FetchOptions"/>, the query will return all available anchors. If you provide multiple options, the
    /// result is the logical AND of those options.
    ///
    /// For example, if you specify an array of <see cref="Uuids"/> and a <see cref="SingleComponentType"/>, then
    /// the result will be anchors that match any of those UUIDs that also support that component type.
    ///
    /// Note that the fields prefixed with `Single` are the same as providing an array of length 1. This is useful in
    /// the common cases of retrieving a single anchor by UUID, or querying for all anchors of a single component
    /// type, without having to allocate a managed array to hold that single element.
    ///
    /// <example>
    /// For example, these two are equivalent queries:
    /// <code><![CDATA[
    /// async void FetchByUuid(Guid uuid) {
    ///   var options1 = new OVRAnchor.FetchOptions {
    ///     SingleUuid = uuid
    ///   };
    ///
    ///   var options2 = new OVRAnchor.FetchOptions {
    ///     Uuids = new Guid[] { uuid }
    ///   };
    ///
    ///   // Both options1 and options2 will perform the same query and return the same result
    ///   var result1 = await OVRAnchor.FetchAnchorsAsync(new List<OVRAnchor>(), options1);
    ///   var result2 = await OVRAnchor.FetchAnchorsAsync(new List<OVRAnchor>(), options2);
    ///
    ///   Debug.Assert(result1.Status == result2.Status);
    ///   if (result1.Success)
    ///   {
    ///       Debug.Assert(result1.Value.SequenceEqual(result2.Value));
    ///   }
    /// }
    /// ]]></code>
    /// </example>
    /// </remarks>
    public struct FetchOptions
    {
        /// <summary>
        /// A UUID of an existing anchor to fetch.
        /// </summary>
        /// <remarks>
        /// Set this to fetch a single anchor with by UUID. If you want to fetch multiple anchors by UUID, use
        /// <see cref="Uuids"/>.
        /// </remarks>
        public Guid? SingleUuid;

        /// <summary>
        /// A collection of UUIDS to fetch.
        /// </summary>
        /// <remarks>
        /// If you want to retrieve only a single UUID, you can <see cref="SingleUuid"/> to avoid having to create
        /// a temporary container of length one.
        ///
        /// NOTE: Only the first 50 anchors are processed by
        /// <see cref="OVRAnchor.FetchAnchorsAsync(System.Collections.Generic.List{OVRAnchor},OVRAnchor.FetchOptions,System.Action{System.Collections.Generic.List{OVRAnchor},int})"/>
        /// </remarks>
        public IEnumerable<Guid> Uuids;

        /// <summary>
        /// Fetch anchors that support a given component type.
        /// </summary>
        /// <remarks>
        /// Each anchor supports one or more anchor types (types that implemented <see cref="IOVRAnchorComponent{T}"/>).
        ///
        /// If not null, <see cref="SingleComponentType"/> must be a type that implements
        /// <see cref="IOVRAnchorComponent{T}"/>, e.g., <see cref="OVRBounded2D"/> or <see cref="OVRRoomLayout"/>.
        ///
        /// If you have multiple component types, use <see cref="ComponentTypes"/> instead.
        /// </remarks>
        public Type SingleComponentType;

        /// <summary>
        /// Fetch anchors that support a given set of component types.
        /// </summary>
        /// <remarks>
        /// Each anchor supports one or more anchor types (types that implemented <see cref="IOVRAnchorComponent{T}"/>).
        ///
        /// If not null, <see cref="ComponentTypes"/> must be a collection of types that implement
        /// <see cref="IOVRAnchorComponent{T}"/>, e.g., <see cref="OVRBounded2D"/> or <see cref="OVRRoomLayout"/>.
        ///
        /// When multiple components are specified, all anchors that support any of those types are returned, i.e.,
        /// the component types are OR'd together to determine whether an anchor matches.
        ///
        /// If you only have a single component type, you can use <see cref="SingleComponentType"/> to avoid having
        /// to create a temporary container of length one.
        /// </remarks>
        public IEnumerable<Type> ComponentTypes;


        // DiscoverSpaces has an upper limit, requiring batching if exceeded
        private const int MaximumUuidCount = 50;

        /// <summary>
        /// Creates a batch of DiscoverSpaces calls.
        ///
        /// Batches are currently needed when we have too many UUIDS (<see cref="MaximumUuidCount"/>).
        /// </summary>
        internal unsafe void DiscoverSpaces(List<(Result, ulong)> batches)
        {
            batches.Clear();

            var telemetryMarker = OVRTelemetry.Start((int)Telemetry.MarkerId.DiscoverSpaces);

            // Stores the filters
            using var filterStorage = new OVRNativeList<FilterUnion>(Allocator.Temp);

            // Stores only the uuid filters (possibly batched)
            using var uuidFilterStorage = new OVRNativeList<FilterUnion>(Allocator.Temp);

            // Pointers to the filters in filterStorage
            using var filters = new OVRNativeList<IntPtr>(Allocator.Temp);

            // first we aggregate the non-uuid filters (will be reused)
            using var spaceComponentTypes = OVRNativeList.WithSuggestedCapacityFrom(ComponentTypes).AllocateEmpty<long>(Allocator.Temp);
            if (SingleComponentType != null)
            {
                var spaceComponentType = GetSpaceComponentType(SingleComponentType);
                spaceComponentTypes.Add((long)spaceComponentType);

                filterStorage.Add(new FilterUnion
                {
                    ComponentFilter = new SpaceDiscoveryFilterInfoComponents
                    {
                        Type = SpaceDiscoveryFilterType.Component,
                        Component = spaceComponentType,
                    }
                });
            }

            foreach (var componentType in ComponentTypes.ToNonAlloc())
            {
                var spaceComponentType = GetSpaceComponentType(componentType);
                spaceComponentTypes.Add((long)spaceComponentType);

                filterStorage.Add(new FilterUnion
                {
                    ComponentFilter = new SpaceDiscoveryFilterInfoComponents
                    {
                        Type = SpaceDiscoveryFilterType.Component,
                        Component = spaceComponentType,
                    }
                });
            }
            telemetryMarker.AddAnnotation(Telemetry.Annotation.ComponentTypes, spaceComponentTypes.Data,
                spaceComponentTypes.Count);


            using var uuidsList = Uuids.ToNativeList(Allocator.Temp);
            if (SingleUuid.HasValue)
            {
                uuidsList.Add(SingleUuid.Value);
            }
            var uuids = uuidsList.AsNativeArray();

            telemetryMarker.AddAnnotation(Telemetry.Annotation.UuidCount, uuids.Length);

            var totalFilterCount = filterStorage.Count;
            if (uuids.Length != 0)
            {
                totalFilterCount++;
            }
            telemetryMarker.AddAnnotation(Telemetry.Annotation.TotalFilterCount, totalFilterCount);

            var iterations = 1;
            if (uuids.Length > MaximumUuidCount)
                iterations = Mathf.CeilToInt(uuids.Length / (float)MaximumUuidCount);

            // now create uuid-specific filters and create batches of requests
            for (var i = 0; i < iterations; i++)
            {
                uuidFilterStorage.Clear();
                if (SingleUuid != null || Uuids != null)
                {
                    // get a subset of the filters for this query
                    var startingIndex = i * MaximumUuidCount;
                    var length = MaximumUuidCount;
                    if (startingIndex + length > uuids.Length)
                        length = uuids.Length - startingIndex;

                    var uuidBatch = uuids.GetSubArray(startingIndex, length);

                    uuidFilterStorage.Add(new FilterUnion
                    {
                        IdFilter = new SpaceDiscoveryFilterInfoIds
                        {
                            Type = SpaceDiscoveryFilterType.Ids,
                            Ids = length == 0 ? null : (Guid*)uuidBatch.GetUnsafePtr(),
                            NumIds = length
                        }
                    });
                }

                // Gather pointers to each filter + uuidfilter
                filters.Clear();
                for (var j = 0; j < filterStorage.Count; j++)
                {
                    filters.Add(new IntPtr(filterStorage.PtrToElementAt(j)));
                }
                for (var j = 0; j < uuidFilterStorage.Count; j++)
                {
                    filters.Add(new IntPtr(uuidFilterStorage.PtrToElementAt(j)));
                }

                var result = OVRPlugin.DiscoverSpaces(new SpaceDiscoveryInfo
                {
                    NumFilters = (uint)filters.Count,
                    Filters = (SpaceDiscoveryFilterInfoHeader**)filters.Data,
                }, out var requestId);

                Telemetry.SetSyncResult(telemetryMarker, requestId, result);

                batches.Add((result, requestId));
            }
        }

        private static SpaceComponentType GetSpaceComponentType(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (!_typeMap.TryGetValue(type, out var componentType))
                throw new ArgumentException(
                    $"{type.FullName} is not a supported anchor component type (IOVRAnchorComponent).", nameof(type));

            return componentType;
        }
    }

    internal static readonly Dictionary<Type, SpaceComponentType> _typeMap = new()
    {
        { typeof(OVRLocatable), SpaceComponentType.Locatable },
        { typeof(OVRStorable), SpaceComponentType.Storable },
        { typeof(OVRSharable), SpaceComponentType.Sharable },
        { typeof(OVRBounded2D), SpaceComponentType.Bounded2D },
        { typeof(OVRBounded3D), SpaceComponentType.Bounded3D },
        { typeof(OVRSemanticLabels), SpaceComponentType.SemanticLabels },
        { typeof(OVRRoomLayout), SpaceComponentType.RoomLayout },
        { typeof(OVRAnchorContainer), SpaceComponentType.SpaceContainer },
        { typeof(OVRTriangleMesh), SpaceComponentType.TriangleMesh },
    };

    [StructLayout(LayoutKind.Explicit)]
    internal struct FilterUnion
    {
        [FieldOffset(0)] public SpaceDiscoveryFilterType Type;
        [FieldOffset(0)] public SpaceDiscoveryFilterInfoComponents ComponentFilter;
        [FieldOffset(0)] public SpaceDiscoveryFilterInfoIds IdFilter;
    }
}
