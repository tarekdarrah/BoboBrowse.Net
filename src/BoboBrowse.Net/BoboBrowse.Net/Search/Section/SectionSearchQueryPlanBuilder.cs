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
namespace BoboBrowse.Net.Search.Section
{
    using BoboBrowse.Net.Support;
    using Lucene.Net.Index;
    using Lucene.Net.Search;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public class SectionSearchQueryPlanBuilder
    {
#if FEATURE_SERIALIZABLE
        [Serializable]
#endif
        public class TranslationException : RuntimeException
        {
            //private static long serialVersionUID = 1L; // NOT USED

            public TranslationException(string message)
                : base(message)
            {
            }
        }

        protected readonly AtomicReader m_reader;
        protected readonly IMetaDataCacheProvider m_cacheProvider;

        public SectionSearchQueryPlanBuilder(AtomicReader reader)
        {
            this.m_reader = reader;
            m_cacheProvider = (reader is IMetaDataCacheProvider ? (IMetaDataCacheProvider)reader : null);
        }

        /// <summary>
        /// Gets a query plan for the given query.
        /// It is assumed that <code>query</code> is already rewritten before this call.
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        public virtual SectionSearchQueryPlan GetPlan(Query query)
        {
            if (query != null)
            {
                SectionSearchQueryPlan textSearchPlan = Translate(query);

                if (!(textSearchPlan is UnaryNotNode))
                {
                    return textSearchPlan;
                }
            }
            return null;
        }

        private SectionSearchQueryPlan Translate(Query query)
        {
            if (query != null)
            {
                if (query is TermQuery)
                {
                    return TranslateTermQuery((TermQuery)query);
                }
                else if (query is PhraseQuery)
                {
                    return TranslatePhraseQuery((PhraseQuery)query);
                }
                else if (query is BooleanQuery)
                {
                    return TranslateBooleanQuery((BooleanQuery)query);
                }
                else if (query is MetaDataQuery)
                {
                    MetaDataQuery mquery = (MetaDataQuery)query;
                    IMetaDataCache cache = (m_cacheProvider != null ? m_cacheProvider.Get(mquery.Term) : null);

                    if (cache != null)
                    {
                        return ((MetaDataQuery)query).GetPlan(cache);
                    }
                    else
                    {
                        return ((MetaDataQuery)query).GetPlan(m_reader);
                    }
                }
                else
                {
                    throw new TranslationException("unable to translate Query class: " + query.GetType().Name);
                }
            }
            return null;
        }

        private SectionSearchQueryPlan TranslateTermQuery(TermQuery query)
        {
            return new TermNode(query.Term, m_reader);
        }

        private SectionSearchQueryPlan TranslatePhraseQuery(PhraseQuery query)
        {
            Term[] terms = query.GetTerms();
            TermNode[] nodes = new TermNode[terms.Length];
            int[] positions = query.GetPositions();
            for (int i = 0; i < terms.Length; i++)
            {
                nodes[i] = new TermNode(terms[i], positions[i], m_reader);
            }
            return new PhraseNode(nodes, m_reader);
        }

        private SectionSearchQueryPlan TranslateBooleanQuery(BooleanQuery query)
        {
            List<Query> requiredClauses = new List<Query>();
            List<Query> prohibitedClauses = new List<Query>();
            List<Query> optionalClauses = new List<Query>();
            BooleanClause[] clauses = query.GetClauses();
            foreach (BooleanClause clause in clauses)
            {
                if (clause.IsRequired)
                {
                    requiredClauses.Add(clause.Query);
                }
                else if (clause.IsProhibited)
                {
                    prohibitedClauses.Add(clause.Query);
                }
                else
                {
                    optionalClauses.Add(clause.Query);
                }
            }

            SectionSearchQueryPlan positiveNode = null;
            SectionSearchQueryPlan negativeNode = null;

            if (requiredClauses.Count > 0)
            {
                if (requiredClauses.Count == 1)
                {
                    positiveNode = Translate(requiredClauses.Get(0));
                }
                else
                {
                    SectionSearchQueryPlan[] subqueries = Translate(requiredClauses);
                    if (subqueries != null && subqueries.Length > 0) positiveNode = new AndNode(subqueries);
                }
            }
            else if (optionalClauses.Count > 0)
            {
                if (optionalClauses.Count == 1)
                {
                    positiveNode = Translate(optionalClauses.Get(0));
                }
                else
                {
                    SectionSearchQueryPlan[] subqueries = Translate(optionalClauses);
                    if (subqueries != null && subqueries.Length > 0) positiveNode = new OrNode(subqueries);
                }
            }

            if (prohibitedClauses.Count > 0)
            {
                if (prohibitedClauses.Count == 1)
                {
                    negativeNode = Translate(prohibitedClauses.Get(0));
                }
                else
                {
                    negativeNode = new OrNode(Translate(prohibitedClauses));
                }
            }

            if (negativeNode == null)
            {
                return positiveNode;
            }
            else
            {
                if (positiveNode == null)
                {
                    return new UnaryNotNode(negativeNode);
                }
                else
                {
                    return new AndNotNode(positiveNode, negativeNode);
                }
            }
        }

        private SectionSearchQueryPlan[] Translate(ICollection<Query> queries)
        {
            int size = queries.Count;
            List<SectionSearchQueryPlan> result = new List<SectionSearchQueryPlan>(size);
            for (int i = 0; i < size; i++)
            {
                SectionSearchQueryPlan plan = Translate(queries.Get(i));
                if (plan != null) result.Add(plan);
            }
            return result.ToArray();
        }
    }
}
