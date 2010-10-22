﻿using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Raven.Munin.Tree;

namespace Raven.Munin
{
    public class SecondaryIndex
    {
        private readonly string indexDef;
        private readonly IPersistentSource persistentSource;
        private readonly Func<JToken, IComparable> transform;

        public SecondaryIndex(Func<JToken, IComparable> transform, string indexDef, IPersistentSource persistentSource)
        {
            this.transform = transform;
            this.indexDef = indexDef;
            this.persistentSource = persistentSource;
        }

        private IBinarySearchTree<IComparable, IBinarySearchTree<JToken, JToken>> Index
        {
            get { return persistentSource.DictionariesStates[DictionaryId].SecondaryIndicesState[IndexId]; }
            set { persistentSource.DictionariesStates[DictionaryId].SecondaryIndicesState[IndexId] = value; }
        }

        public long Count
        {
            get { return Index.Count; }
        }

        public int DictionaryId { get; set; }

        public int IndexId { get; set; }

        public override string ToString()
        {
            return indexDef + " (" + Index.Count + ")";
        }

        public void Add(JToken key)
        {
            IComparable actualKey = transform(key);
            Index = Index.AddOrUpdate(actualKey, 
                new EmptyAVLTree<JToken, JToken>(JTokenComparer.Instance).Add(key,key),
                (comparable, tree) => tree.Add(key, key));
        }

        public void Remove(JToken key)
        {
            IComparable actualKey = transform(key);
            var result = Index.Search(actualKey);
            if (result.IsEmpty)
            {
                return;
            }
            bool removed;
            JToken _;
            var removedResult = result.Value.TryRemove(key, out removed, out _);
            if(removedResult.IsEmpty)
            {
                IBinarySearchTree<JToken, JToken> ignored;
                Index = Index.TryRemove(actualKey, out removed, out ignored);
            }
            else
            {
                Index = Index.AddOrUpdate(actualKey, removedResult, (comparable, tree) => removedResult);
            }
        }


        public IEnumerable<JToken> SkipFromEnd(int start)
        {
            return Index.ValuesInReverseOrder.Skip(start).Select(item => item.Key);
        }

        public IEnumerable<JToken> SkipAfter(JToken key)
        {
            return Index.GreaterThan(transform(key)).SelectMany(binarySearchTree => binarySearchTree.ValuesInOrder);
        }

        public IEnumerable<JToken> SkipTo(JToken key)
        {
            return Index.GreaterThanOrEqual(transform(key)).SelectMany(binarySearchTree => binarySearchTree.ValuesInOrder);
        }

        public JToken LastOrDefault()
        {
            return persistentSource.Read(_ =>
            {
                if (Index.RightMost.IsEmpty)
                    return null;
                var binarySearchTree = Index.RightMost.Value.RightMost;
                if (binarySearchTree.IsEmpty)
                    return null;
                return binarySearchTree.Value;
            });
        }

        public JToken FirstOrDefault()
        {
            return persistentSource.Read(_ =>
            {
                if (Index.LeftMost.IsEmpty)
                    return null;
                var binarySearchTree = Index.LeftMost.Value.LeftMost;
                if (binarySearchTree.IsEmpty)
                    return null;
                return binarySearchTree.Value;
            });
        }

        public void Initialize(int dictionaryId, int indexId)
        {
            DictionaryId = dictionaryId;
            IndexId = indexId;
        }
    }
}