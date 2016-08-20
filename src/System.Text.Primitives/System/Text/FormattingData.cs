// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Text
{
    public struct FormattingData
    {
        private static FormattingData s_invariantUtf16;
        private static FormattingData s_invariantUtf8;
        private byte[][] _digitsAndSymbols; // this could be flattened into a single array
        private List<TrieNode> _parsingTrieList; // The parent nodes of the trie
        private List<TrieNode> _childTrieNodes; // The child nodes of the trie. These two lists could be flattened into one
                                                // but I went with this architecture because it is very efficient for the common flat-trie case
        private Encoding _encoding;

        public enum Encoding
        {
            Utf16 = 0,
            Utf8 = 1,
        }
        // used to determine if/how we should construct the parsing trie
        // eventually, this could include all sorts of information, like encoding rules, etc.
        public enum ParsingStates
        {
            NO_PARSING = 0, // don't construct it
            PARSING = 1,    // construct it with upfront cost
            LAZY_PARSING = 3 // construct it but allocate lazy. 3 is chosen so the bitwise check below work
        }

        public FormattingData(byte[][] digitsAndSymbols, Encoding encoding, byte parsing = (byte)ParsingStates.NO_PARSING)
        {
            _digitsAndSymbols = digitsAndSymbols;
            _encoding = encoding;
            if (((byte)parsing & (byte)ParsingStates.PARSING) == 1) // both PARSING and LAZY_PARSING pass this check
            {
                _parsingTrieList = new List<TrieNode>();
                _childTrieNodes = new List<TrieNode>();
                ConstructParsingTrie(parsing == (byte)ParsingStates.LAZY_PARSING);
            }
            else
            {
                _parsingTrieList = null;
                _childTrieNodes = null;
            }
        }
        /// <summary>
        /// Construct a formatting data by specifying the bytes for each value
        /// </summary>
        /// <param name="digit0">The bytes which map to the digit zero (0)</param>
        /// <param name="digit1">The bytes which map to the digit one (1)</param>
        /// <param name="digit2">The bytes which map to the digit two (2)</param>
        /// <param name="digit3">The bytes which map to the digit three (3)</param>
        /// <param name="digit4">The bytes which map to the digit four (4)</param>
        /// <param name="digit5">The bytes which map to the digit five (5)</param>
        /// <param name="digit6">The bytes which map to the digit six (6)</param>
        /// <param name="digit7">The bytes which map to the digit seven (7)</param>
        /// <param name="digit8">The bytes which map to the digit eight (8)</param>
        /// <param name="digit9">The bytes which map to the digit nine (9)</param>
        /// <param name="decimalSeparator">The bytes which map to the decimal separtor.</param>
        /// <param name="groupSeparator">The bytes which map to the group separator</param>
        /// <param name="infinity">The bytes which map to the representation of infinity</param>
        /// <param name="minusSign">The bytes which map to the minus sign</param>
        /// <param name="plusSign">The bytes which map to the plus sign</param>
        /// <param name="nan">The bytes which map to the representation of NaN (not a number)</param>
        /// <param name="exponent">The bytes which map to the exponent marker for floating-point types</param>
        /// <param name="encoding">The specified encoding</param>
        /// <param name="parsing"></param>
        public FormattingData(byte[] digit0, byte[] digit1, byte[] digit2, byte[] digit3, byte[] digit4, byte[] digit5, byte[] digit6, byte[] digit7,
            byte[] digit8, byte[] digit9, byte[] decimalSeparator, byte[] groupSeparator, byte[] infinity, byte[] minusSign, byte[] plusSign, byte[] nan,
            byte[] exponent, byte[] exponentSecondary, Encoding encoding, byte parsing = (byte)ParsingStates.NO_PARSING) 
            : this(new byte[][] { digit0, digit1, digit2, digit3, digit4, digit5, digit6, digit7, digit8, digit9,
                decimalSeparator, groupSeparator, infinity, minusSign, plusSign, nan, exponent, exponentSecondary }, encoding, parsing)
        {
            // This constructor simple calls the base constructor
        }
        private class TrieNode
        {
            public byte? ByteValue { get; set; } // nullable byte is used so that the null-value can be used           (0x02)
                                                 // to represent a prefixed value for example, if two byte           /       \
                                                 // sequences { 0x01 } and { 0x01, 0x02} are used                 (null)    (0x03)
                                                 // in the encoding, the trie will be constructed as shown        {0x01} {0x01, 0x02}
            public List<int> ChildIndices { get; set; } // these values index into one of two things:
                                                        // 1) if IsLeaf is false, this is a branch, which means these index into _childTrieNodes and
                                                        //    refer to the children of these nodes
                                                        // 2) if IsLeaf is true, this is a leaf, which means these index into _digitsAndSymbols and
                                                        //    refer to specific digit or symbolic values
                                                        // int indices are used so that we only have two List<TrieNodes>
            public bool IsLeaf { get; set; }

            public TrieNode(byte? byteValue, int index, bool isLeaf = true)
            {
                ByteValue = byteValue;
                ChildIndices = new List<int>(new int[] { index }); // indicies are stored as ints (as opposed to bytes) because you can have arbitrarily large tries
                IsLeaf = isLeaf;
            }
        }

        private void ConstructParsingTrie(bool lazily = false)
        {
            // TODO: Implement lazy construction

            int skippedValues = 0;
            // We construct the initial layer of the trie using an insertion sort. This has two benefits:
            // 1. _parsingTrie is now pre-sorted via insertion sort, which is fast for arrays of small size
            // 2. We are alerted of any collisions that occur, which means we can construct the trie efficiently
            for (int i = 0; i < _digitsAndSymbols.Length; i++)
            {
                // If the encoding doesn't specify a particular symbol, we skip over it. This allows for optional parameters
                if (_digitsAndSymbols[i] == null || _digitsAndSymbols.Length == 0)
                {
                    skippedValues++;
                    continue;
                }
                byte temp = _digitsAndSymbols[i][0];
                int j;
                // the insertion sort loop. This is pretty standard
                for (j = i - skippedValues - 1; j >= 0 && _parsingTrieList[j].ByteValue > temp; j--)
                {
                    if (j + 1 == _parsingTrieList.Count) // if we're at the end of the list, we append it
                        _parsingTrieList.Add(_parsingTrieList[j]);
                    else // otherwise, we swap
                        _parsingTrieList[j + 1] = _parsingTrieList[j];
                }
                if (_parsingTrieList.Count < 1) // if this is the first element, we should just add it to the list
                {
                    _parsingTrieList.Add(new TrieNode(temp, i));
                }
                else if (j == -1) // we have to check for this before the collision check so that we don't throw an index error
                {
                    _parsingTrieList[j + 1] = new TrieNode(temp, i); // this completes the swap
                }
                else if (_parsingTrieList[j].ByteValue == temp) // if a collision occurred
                {
                    CreateChildNode(_parsingTrieList[j], i, 1); // we need to create a child node (start building the trie)
                    skippedValues++;
                }
                else if (j >= _parsingTrieList.Count - 1) // if we're at the end of the list, then we just append it
                {
                    _parsingTrieList.Add(new TrieNode(temp, i));
                }
                else
                {
                    _parsingTrieList[j + 1] = new TrieNode(temp, i); // this completes the swap
                }
            }
        }
        private void CreateChildNode(TrieNode node, int newIndex, int level)
        {
            CreateChildNode(node, newIndex, node.ChildIndices[0], level);
        }
        private void CreateChildNode(TrieNode node, int newIndex, int oldIndex, int level)
        {
            byte? newByteValue = DisambiguateByteValue(newIndex, level); // this is the value we're attempting to place in the trie

            // If the node is a leaf, we know we have reached the bottom of the trie.
            if (node.IsLeaf)
            {
                node.IsLeaf = false; // this is now a branch, not a leaf
                byte? oldByteValue = DisambiguateByteValue(oldIndex, level);
                if (newByteValue == oldByteValue)   // this means there's another collision, so we have to go down one level more
                {
                    if (oldByteValue == null)   // if both values are null, we have identical code units, which is bad
                    {
                        throw new ArgumentException("Invalid mapping: two values map to the same code unit.");
                    }
                    else
                    {
                        TrieNode newNode = new TrieNode(newByteValue, oldIndex);    // we create a new node. it references the old index for now since we want that
                        CreateChildNode(newNode, newIndex, level + 1);              // index to propagate recursively when we call this again
                        _childTrieNodes.Add(newNode);                               // after the recursion is complete, we add our new node to the list
                        node.ChildIndices.Clear();                                              // we clear the old values
                        node.ChildIndices.AddRange(new int[] { _childTrieNodes.Count - 1 });    // and add the reference to the newly constructed node
                        return;
                    }
                }
                else if (oldByteValue == null || oldByteValue < newByteValue)   // if there isn't a collision, we select the order we want to insert these nodes into
                {                                                               // the list. This makes it so that our indices are sorted, which allows us to binary
                    _childTrieNodes.Add(new TrieNode(oldByteValue, oldIndex));  // search the indices to recover info in O(logN).
                    _childTrieNodes.Add(new TrieNode(newByteValue, newIndex));
                }
                else
                {
                    _childTrieNodes.Add(new TrieNode(newByteValue, newIndex));
                    _childTrieNodes.Add(new TrieNode(oldByteValue, oldIndex));
                }
                node.ChildIndices.Clear();
                node.ChildIndices.AddRange(new int[] { _childTrieNodes.Count - 2, _childTrieNodes.Count - 1 }); // we now have two branches coming from this node
                return;
            }
            else    // if this is not a leaf, we need to add a new branch to the appropriate spot
            {
                var search = BinarySearch(node, level, newByteValue);   // we search to see if this value already exists in the branches

                if (search.Item1)   // if it does exist within the branches
                {
                    CreateChildNode(_childTrieNodes[node.ChildIndices[search.Item2]], newIndex, level + 1); // we need to recurse down that branch
                }
                else    // if it doesn't exist, then we get to add a new branch to this node
                {
                    _childTrieNodes.Add(new TrieNode(newByteValue, newIndex));          // we create the new node
                    node.ChildIndices.Insert(search.Item2, _childTrieNodes.Count - 1);  // and then insert at the appropriate index to keep the indices sorted
                    return;
                }
            }
            
        }
        private byte? DisambiguateMiddleValue(TrieNode node, int level, int m)
        {
            if (node.IsLeaf)
                return DisambiguateByteValue(node.ChildIndices[m], level);
            else
                return _childTrieNodes[node.ChildIndices[m]].ByteValue;
        }
        // This binary search implementation returns a tuple:
        // * a bool which represents whether the search completed successfully
        // * an int representing either:
        //      - the index of the item searched for
        //      - the index of the location where the item should be placed to maintain a sorted list
        // Because of the second function, a tuple is necessary since we cannot return -1 to represent a failed search 
        private Tuple<bool, int> BinarySearch(TrieNode node, int level, byte? value)
        {
            int leftBound = 0, rightBound = node.ChildIndices.Count - 1;
            int midIndex = 0;
            while (true)
            {
                if (leftBound > rightBound)  // if the search failed
                {
                    // this loop is necessary because binary search takes the floor
                    // of the middle, which means it can give incorrect indices for insertion.
                    // we should never iterate up more than two indices.
                    while (midIndex < node.ChildIndices.Count)
                    {
                        byte? middleValue = DisambiguateMiddleValue(node, level, midIndex);

                        if (middleValue > value)
                            break;
                        midIndex++;
                    }
                    return new Tuple<bool, int>(false, midIndex);
                }

                midIndex = (leftBound + rightBound) / 2; // find the middle value

                byte? mValue;
                if (node.IsLeaf)
                    mValue = DisambiguateByteValue(node.ChildIndices[midIndex], level);
                else
                    mValue = _childTrieNodes[node.ChildIndices[midIndex]].ByteValue;

                if (mValue < value)
                    leftBound = midIndex + 1;
                else if (mValue > value)
                    rightBound = midIndex - 1;
                else if (mValue == value)
                    return new Tuple<bool, int>(true, midIndex);
                else // one of the values is null and they are != to each other
                {
                    if (mValue == null) // mValue < value
                        leftBound = midIndex + 1;
                    else // mValue > value (since value == null)
                        rightBound = midIndex - 1;
                }
            }
        }
        // This binary search implementation searches _parsingTrieList rather than
        // a specific node. This should help with performances for cases where there
        // is a flat trie, as we operate directly on the sorted list of nodes rather than
        // on a node which contains a sorted list of indices into a separate list.
        private Tuple<bool, int> BinarySearch(byte? value)
        {
            int leftBound = 0, rightBound = _parsingTrieList.Count - 1;
            int midIndex = 0;
            while (true)
            {
                if (leftBound > rightBound)
                {
                    byte? middleValue;
                    do
                        middleValue = _parsingTrieList[midIndex++].ByteValue;
                    while ( middleValue < value);
                    return new Tuple<bool, int>(false, midIndex);
                }

                midIndex = (leftBound + rightBound) / 2;

                byte? mValue = _parsingTrieList[midIndex].ByteValue;

                if (mValue < value)
                    leftBound = midIndex + 1;
                else if (mValue > value)
                    rightBound = midIndex - 1;
                else if (mValue == value)
                    return new Tuple<bool, int>(true, midIndex);
                else // one of the values is null and they are != to each other
                {
                    if (mValue == null) // mValue < value
                        leftBound = midIndex + 1;
                    else // mValue > value (since value == null)
                        rightBound = midIndex - 1;
                }
            }
        }
        private byte? DisambiguateByteValue(int index, int level)
        {
            if (level >= _digitsAndSymbols[index].Length)
            {
                return null;
            }
            else
            {
                return _digitsAndSymbols[index][level];
            }
        }

        // it might be worth compacting the data into a single byte array.
        // Also, it would be great if we could freeze it.
        static FormattingData()
        {
            var utf16digitsAndSymbols = new byte[][] {
                new byte[] { 48, 0, }, // digit 0
                new byte[] { 49, 0, },
                new byte[] { 50, 0, },
                new byte[] { 51, 0, },
                new byte[] { 52, 0, },
                new byte[] { 53, 0, },
                new byte[] { 54, 0, },
                new byte[] { 55, 0, },
                new byte[] { 56, 0, },
                new byte[] { 57, 0, }, // digit 9
                new byte[] { 46, 0, }, // decimal separator
                new byte[] { 44, 0, }, // group separator
                new byte[] { 73, 0, 110, 0, 102, 0, 105, 0, 110, 0, 105, 0, 116, 0, 121, 0, }, // Infinity
                new byte[] { 45, 0, }, // minus sign 
                new byte[] { 43, 0, }, // plus sign 
                new byte[] { 78, 0, 97, 0, 78, 0, }, // NaN
                new byte[] { 69, 0, }, // E
                new byte[] { 101, 0, }, // e
            };

            s_invariantUtf16 = new FormattingData(utf16digitsAndSymbols, Encoding.Utf16);

            var utf8digitsAndSymbols = new byte[][] {
                new byte[] { 48, },
                new byte[] { 49, },
                new byte[] { 50, },
                new byte[] { 51, },
                new byte[] { 52, },
                new byte[] { 53, },
                new byte[] { 54, },
                new byte[] { 55, },
                new byte[] { 56, },
                new byte[] { 57, }, // digit 9
                new byte[] { 46, }, // decimal separator
                new byte[] { 44, }, // group separator
                new byte[] { 73, 110, 102, 105, 110, 105, 116, 121, },
                new byte[] { 45, }, // minus sign
                new byte[] { 43, }, // plus sign
                new byte[] { 78, 97, 78, }, // NaN
                new byte[] { 69, }, // E
                new byte[] { 101, }, // e
            };

            s_invariantUtf8 = new FormattingData(utf8digitsAndSymbols, Encoding.Utf8);
        }

        public static FormattingData InvariantUtf16
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return s_invariantUtf16;
            }
        }
        public static FormattingData InvariantUtf8
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return s_invariantUtf8;
            }
        }

        /// <summary>
        /// Parse the next byte in a byte array. Will return either a DigitOrSymbol value, an InvalidCharacter, or a Continue
        /// </summary>
        /// <param name="nextByte">The next byte to be parsed</param>
        /// <param name="bytesParsed">The total number of bytes parsed (will be zero until a code unit is deciphered)</param>
        /// <returns></returns>
        public byte ParseNextByte(byte nextByte, out int bytesParsed, ref int nodeIndex, ref int level)
        {
            bytesParsed = 0;

            Tuple<bool, int> search;
            TrieNode node = null;
            TrieNode currentNode = null;
            if (nodeIndex == -1) // if we haven't parsed any bytes yet, we should look through _parsingTrie
            {
                search = BinarySearch(nextByte);    // we search the _parsingTrie for the nextByte
                if (search.Item1)
                    node = _parsingTrieList[search.Item2];  // if we find a node, we set that node here
            }
            else    // if we have started parsing bytes, we want to search on the current node and level
            {
                if (level == 1)
                    currentNode = _parsingTrieList[nodeIndex];
                else
                    currentNode = _childTrieNodes[nodeIndex];

                search = BinarySearch(currentNode, level, nextByte);
                if (search.Item1)
                    node = _childTrieNodes[currentNode.ChildIndices[search.Item2]];
            }

            if (search.Item1)   // if we found a node
            {
                if (node.IsLeaf)    // if we're on a leaf, we've found our value and completed the code unit
                {
                    nodeIndex = -1;
                    level = 0;
                    bytesParsed = _digitsAndSymbols[node.ChildIndices[0]].Length;   // report how many bytes this code unit was to the user
                    return (byte)node.ChildIndices[0];  // return the parsed value
                }
                else    // if we're on a branch, we need to keep evaluating
                {
                    if (level == 0)
                        nodeIndex = search.Item2;
                    else
                        nodeIndex = currentNode.ChildIndices[search.Item2];
                    level++;
                    return (byte)Symbol.Continue;
                }
            }
            else    // if we didn't find a node, this isn't a valid code unit (this will change when we do lazy parsing obviously)
            {
                return (byte)Symbol.Invalid;
            }
        }
        public bool VerifyCodeUnit(ref byte[] buffer, int index, int codeUnitIndex, int bytesConsumed, int codeUnitLength)
        {
            if (codeUnitLength == bytesConsumed)
                return true;
            
            for (int i = 0; i < codeUnitLength - bytesConsumed; i++)
            {
                if (buffer[i + index] != _digitsAndSymbols[codeUnitIndex][i + bytesConsumed])
                    return false;
            }

            return true;
        }

        public bool TryWriteDigit(ulong digit, Span<byte> buffer, out int bytesWritten)
        {
            Precondition.Require(digit < 10);
            return TryWriteDigitOrSymbol(digit, buffer, out bytesWritten);
        }

        public bool TryWriteSymbol(Symbol symbol, Span<byte> buffer, out int bytesWritten)
        {
            var symbolIndex = (ushort)symbol;
            return TryWriteDigitOrSymbol(symbolIndex, buffer, out bytesWritten);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryWriteDigitOrSymbol(ulong digitOrSymbolIndex, Span<byte> buffer, out int bytesWritten)
        {
            byte[] bytes = _digitsAndSymbols[digitOrSymbolIndex];
            bytesWritten = bytes.Length;
            if (bytesWritten > buffer.Length)
            {
                bytesWritten = 0;
                return false;
            }

            if (bytesWritten == 2)
            {
                buffer[0] = bytes[0];
                buffer[1] = bytes[1];
                return true;
            }

            if (bytesWritten == 1)
            {
                buffer[0] = bytes[0];
                return true;
            }

            buffer.Set(bytes);
            return true;
        }

        public enum Symbol : ushort
        {
            DecimalSeparator = 10,
            GroupSeparator = 11,
            InfinitySign = 12,
            MinusSign = 13,
            PlusSign = 14,          
            NaN = 15,
            Exponent = 16,
            ExponentSecondary = 17,
            Invalid = 18, // invalid character for parsing
            Continue = 19, // continue character for state machine
        }

        public bool IsInvariantUtf16
        {
            get { return _digitsAndSymbols == s_invariantUtf16._digitsAndSymbols; }
        }
        public bool IsInvariantUtf8
        {
            get { return _digitsAndSymbols == s_invariantUtf8._digitsAndSymbols; }
        }

        public bool IsUtf16
        {
            get { return _encoding == Encoding.Utf16; }
        }
        public bool IsUtf8
        {
            get { return _encoding == Encoding.Utf8; }
        }
    }
}
