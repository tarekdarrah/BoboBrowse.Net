﻿//* Bobo Browse Engine - High performance faceted/parametric search implementation 
//* that handles various types of semi-structured data.  Originally written in Java.
//*
//* Ported and adapted for C# by Shad Storhaug.
//*
//* Copyright (C) 2005-2015  John Wang
//*
//* Licensed under the Apache License, Version 2.0 (the "License");
//* you may not use this file except in compliance with the License.
//* You may obtain a copy of the License at
//*
//*   http://www.apache.org/licenses/LICENSE-2.0
//*
//* Unless required by applicable law or agreed to in writing, software
//* distributed under the License is distributed on an "AS IS" BASIS,
//* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//* See the License for the specific language governing permissions and
//* limitations under the License.

// Version compatibility level: 4.0.2
namespace BoboBrowse.Net.Facets.Impl
{
    using BoboBrowse.Net.Facets.Filter;
    using BoboBrowse.Net.Sort;
    using BoboBrowse.Net.Support;
    using Lucene.Net.Support;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    public class ComboFacetHandler : FacetHandler<FacetDataNone>
    {
        private const string DFEAULT_SEPARATOR = ":";
	    private readonly string m_separator;

        /// <summary>
        /// Initializes a new instance of <see cref="T:ComboFacetHandler"/>. The separator will be assumed to be ":".
        /// </summary>
        /// <param name="name">The facet handler name. Must be the same value as the Lucene.Net index field name.</param>
        /// <param name="dependsOn">List of facets this one depends on for loading.</param>
        public ComboFacetHandler(string name, ICollection<string> dependsOn)
            : this(name, DFEAULT_SEPARATOR, dependsOn)
        {}

        /// <summary>
        /// Initializes a new instance of <see cref="T:ComboFacetHandler"/>.
        /// </summary>
        /// <param name="name">The facet handler name. Must be the same value as the Lucene.Net index field name.</param>
        /// <param name="separator">The separator that is used to delineate the values of the different index fields.</param>
        /// <param name="dependsOn">List of facets this one depends on for loading.</param>
        public ComboFacetHandler(string name, string separator, ICollection<string> dependsOn)
            : base(name, dependsOn)
        {
            m_separator = separator;
        }

        public virtual string Separator
        {
            get { return m_separator; }
        }

        private class ComboSelection
        {
            private readonly string m_name;
		    private readonly string m_val;

            public string Name
            {
                get { return this.m_name; }
            }

            public string Value
            {
                get { return this.m_val; }
            }

            private ComboSelection(string name, string val)
            {
                this.m_name = name;
                this.m_val = val;
            }

            public static ComboSelection Parse(string value, string sep)
            {
                var splitString = value.Split(new string[] { sep }, StringSplitOptions.RemoveEmptyEntries);
                string name = splitString.Length > 0 ? splitString[0] : null;
                string val = splitString.Length > 1 ? splitString[1] : null;

                if (name != null && val != null)
                {
                    return new ComboSelection(name, val);
                }
                return null;
            }
        }

        public override RandomAccessFilter BuildRandomAccessFilter(string value, IDictionary<string, string> selectionProperty)
        {
            RandomAccessFilter retFilter = EmptyFilter.Instance;
            ComboSelection comboSel = ComboSelection.Parse(value, m_separator);
            if (comboSel != null)
            {
                IFacetHandler handler = GetDependedFacetHandler(comboSel.Name);
                if (handler != null)
                {
                    retFilter = handler.BuildRandomAccessFilter(comboSel.Value, selectionProperty);
                }
            }
            return retFilter;
        }

        private static IDictionary<string, IList<string>> ConvertMap(string[] vals, string sep)
        {
            IDictionary<string, IList<string>> retmap = new Dictionary<string, IList<string>>();
            foreach (string val in vals)
            {
                ComboSelection sel = ComboSelection.Parse(val, sep);
                if (sel != null)
                {
                    IList<string> valList = retmap.Get(sel.Name);
                    if (valList == null)
                    {
                        valList = new List<string>();
                        retmap.Put(sel.Name, valList);
                    }
                    valList.Add(sel.Value);
                }
            }
            return retmap;
        }

        public override RandomAccessFilter BuildRandomAccessAndFilter(string[] vals, IDictionary<string, string> prop)
        {
            IDictionary<string, IList<string>> valMap = ConvertMap(vals, m_separator);

            List<RandomAccessFilter> filterList = new List<RandomAccessFilter>();
            foreach (var entry in valMap)
            {
                string name = entry.Key;
                IFacetHandler facetHandler = GetDependedFacetHandler(name);
                if (facetHandler == null)
                {
                    return EmptyFilter.Instance;
                }
                IList<string> selVals = entry.Value;
                if (selVals == null || selVals.Count == 0) return EmptyFilter.Instance;
                RandomAccessFilter f = facetHandler.BuildRandomAccessAndFilter(selVals.ToArray(), prop);
                if (f == EmptyFilter.Instance) return f;
                filterList.Add(f);
            }

            if (filterList.Count == 0)
            {
                return EmptyFilter.Instance;
            }
            if (filterList.Count == 1)
            {
                return filterList.Get(0);
            }
            return new RandomAccessAndFilter(filterList);
        }

        public override RandomAccessFilter BuildRandomAccessOrFilter(string[] vals, IDictionary<string, string> prop, bool isNot)
        {
            IDictionary<string, IList<string>> valMap = ConvertMap(vals, m_separator);

            List<RandomAccessFilter> filterList = new List<RandomAccessFilter>();
            foreach (var entry in valMap)
            {
                string name = entry.Key;
                IFacetHandler facetHandler = GetDependedFacetHandler(name);
                if (facetHandler == null)
                {
                    continue;
                }
                IList<string> selVals = entry.Value;
                if (selVals == null || selVals.Count == 0)
                {
                    continue;
                }
                RandomAccessFilter f = facetHandler.BuildRandomAccessOrFilter(selVals.ToArray(), prop, isNot);
                if (f == EmptyFilter.Instance) continue;
                filterList.Add(f);
            }

            if (filterList.Count == 0)
            {
                return EmptyFilter.Instance;
            }
            if (filterList.Count == 1)
            {
                return filterList.Get(0);
            }

            if (isNot)
            {
                return new RandomAccessAndFilter(filterList);
            }
            else
            {
                return new RandomAccessOrFilter(filterList);
            }
        }

        public override DocComparerSource GetDocComparerSource()
        {
            throw new NotSupportedException("sorting not supported for " + typeof(ComboFacetHandler));
        }

        public override FacetCountCollectorSource GetFacetCountCollectorSource(BrowseSelection sel, FacetSpec fspec)
        {
            throw new NotSupportedException("facet counting not supported for " + typeof(ComboFacetHandler));
        }

        public override string[] GetFieldValues(BoboSegmentReader reader, int id)
        {
            IEnumerable<string> dependsOn = this.DependsOn;
            List<string> valueList = new List<string>();
            foreach (string depends in dependsOn)
            {
                IFacetHandler facetHandler = GetDependedFacetHandler(depends);
                string[] fieldValues = facetHandler.GetFieldValues(reader, id);
                foreach (string fieldVal in fieldValues)
                {
                    StringBuilder buf = new StringBuilder();
                    buf.Append(depends).Append(m_separator).Append(fieldVal);
                    valueList.Add(buf.ToString());
                }
            }
            return valueList.ToArray();
        }

        public override int GetNumItems(BoboSegmentReader reader, int id)
        {
            IEnumerable<string> dependsOn = this.DependsOn;
            int count = 0;
            foreach (string depends in dependsOn)
            {
                IFacetHandler facetHandler = GetDependedFacetHandler(depends);
                string[] fieldValues = facetHandler.GetFieldValues(reader, id);
                if (fieldValues != null)
                {
                    count++;
                }
            }
            return count;
        }

        public override FacetDataNone Load(BoboSegmentReader reader)
        {
            return FacetDataNone.Instance;
        }
    }
}
