﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using AdvUtils;
using WordSeg;

namespace StatTermWeightInQuery
{
    class Token
    {
        public string strTerm;
        public double fWeight;
    }

    class QueryItem
    {
        public int freq;
        public string strQuery;
        public List<Token> tokenList;

        public QueryItem()
        {
            tokenList = null;
        }
    }

    class Program
    {
        public static int MIN_QUERY_URL_PAIR_FREQUENCY = 2;
        public static int MIN_CLUSTER_SIZE = 2;
        public static BigDictionary<string, List<QueryItem>> query2Item = new BigDictionary<string, List<QueryItem>>();
        private static StreamWriter sw;
        public static Tokens tokens;
        public static WordSeg.WordSeg wordseg;


        private static string ShowTokenList(List<Token> tokenList)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0;i < tokenList.Count;i++)
            {
                sb.Append(tokenList[i].strTerm);
            }

            return sb.ToString();
        }

        //Check whether aList is sub set of bList.
        private static bool AsubOfB(List<Token> aList, List<Token> bList, List<Token> joinBList)
        {
            joinBList.Clear();
            for (int i = 0; i < aList.Count; i++)
            {
                bool bMatched = false;
                for (int j = 0; j < bList.Count; j++)
                {
                    if (aList[i].strTerm == bList[j].strTerm)
                    {
                        joinBList.Add(bList[j]);
                        bMatched = true;
                        break;
                    }
                }

                if (bMatched == false)
                {
                    return false;
                }
            }
            return true;
        }


        private static bool AsubOfB_2(List<Token> aList, List<Token> bList, List<Token> joinBList)
        {
            joinBList.Clear();
            for (int i = 0; i < aList.Count; i++)
            {
                bool bMatched = false;
                for (int j = 0; j < bList.Count; j++)
                {
                    if (aList[i].strTerm == bList[j].strTerm)
                    {
                        joinBList.Add(aList[i]);
                        bMatched = true;
                        break;
                    }
                }

                if (bMatched == false)
                {
                    return false;
                }
            }
            return true;
        }

        public static void AddQueryList(List<QueryItem> qiList)
        {
            foreach (QueryItem item in qiList)
            {
                string strQuery = item.strQuery.Replace(" ", "").ToLower().Trim();
                if (query2Item.ContainsKey(strQuery) == false)
                {
                    query2Item.Add(strQuery, new List<QueryItem>());
                }
                query2Item[strQuery].Add(item);
            }
        }

        private static void InitializeWordBreaker(string strLexicalDictionary)
        {
            wordseg = new WordSeg.WordSeg();
            wordseg.LoadLexicalDict(strLexicalDictionary, true);
            tokens = wordseg.CreateTokens(1024);
        }

        private static void Main(string[] args)
        {
            if (args.Length != 5)
            {
                Console.WriteLine("StatTermWeightInQuery [Query_ClusterId_Freq FileName] [Query_Term_Weight FileName] [Min Query_ClusterId Frequency] [Min Cluster Frequency] [Word Breaker Lex Dictionary]");
                return;
            }
            MIN_QUERY_URL_PAIR_FREQUENCY = int.Parse(args[2]);
            MIN_CLUSTER_SIZE = int.Parse(args[3]);
            InitializeWordBreaker(args[4]);

            Console.WriteLine("Start to process...");
            StreamReader reader = new StreamReader(args[0]);
            sw = new StreamWriter(args[1], false, Encoding.UTF8);

            string lastUrl = "";
            long recordCnt = 0;
            List<QueryItem> qiList = new List<QueryItem>();
            string strLine = null;
            while ((strLine = reader.ReadLine()) != null)
            {
                strLine = strLine.ToLower().Trim();
                string[] strArray = strLine.Split(new char[] { '\t' });
                if (strArray.Length != 3)
                {
                    Console.WriteLine("Invalidated line: {0}", strLine);
                    continue;
                }

                recordCnt++;
                if (recordCnt % 100000 == 0)
                {
                    Console.WriteLine("{0} Query-Url pair has been processed.", recordCnt);
                }

                QueryItem item = new QueryItem
                {
                    strQuery = strArray[0],
                    freq = int.Parse(strArray[2])
                };
                if (item.freq >= MIN_QUERY_URL_PAIR_FREQUENCY)
                {
                    string strUrl = strArray[1];
                    if ((lastUrl.Length > 0) && (strUrl != lastUrl))
                    {
                        if (qiList.Count >= 2)
                        {
                            //Statistics terms weight in each query-url cluster
                            if (StatTermWeightInQuery(qiList) == true)
                            {
                                AddQueryList(qiList);
                            }
                        }
                        qiList = new List<QueryItem>();
                    }
                    qiList.Add(item);
                    lastUrl = strUrl;
                }
            }
            if (qiList.Count >= 2)
            {
                if (StatTermWeightInQuery(qiList) == true)
                {
                    AddQueryList(qiList);
                }
            }

            Console.WriteLine("Merging clusters...");
            MergeQueryWeight();
            reader.Close();
            sw.Close();
        }

        //A query may have relationship with more than one clicked-url. So it is necessary to
        //merge (query, clicked-url1, term weights1), (query, clicked-url2, term weights2) ... (query, clicked-urlN, term weightN) 
        //into a(query, term weight).
        public static void MergeQueryWeight()
        {
            StreamWriter sw_log = new StreamWriter("log.txt");
            foreach (KeyValuePair<string, List<QueryItem>> pair in query2Item)
            {
                if (pair.Value.Count != 0)
                {
                    if (pair.Value[0].tokenList == null)
                    {
                        //This query is invlidated, ignore it.
                        Console.WriteLine("Invalidated Query Item at index 0");
                        continue;
                    }

                    //Calcuate query's total clicked freq in sum
                    int iTotalFreq = 0;
                    foreach (QueryItem item in pair.Value)
                    {
                        if (item.tokenList != null)
                        {
                            iTotalFreq += item.freq;
                        }
                        else
                        {
                            Console.WriteLine("Invalidated Query Item");
                        }
                    }

                    //Initialize query's token list
                    QueryItem rstQueryItem = new QueryItem();
                    rstQueryItem.tokenList = new List<Token>();
                    for (int i = 0; i < pair.Value[0].tokenList.Count; i++)
                    {
                        Token tkn = new Token();
                        tkn.strTerm = pair.Value[0].tokenList[i].strTerm;
                        tkn.fWeight = 0.0;

                        rstQueryItem.tokenList.Add(tkn);
                    }

                    bool bIgnore = false;
                    for (int i = 0; i < rstQueryItem.tokenList.Count; i++)
                    {
                        for (int j = 0; j < pair.Value.Count; j++)
                        {
                            if (pair.Value[j].tokenList == null)
                            {
                                Console.WriteLine("Invalidated Query Item");
                                continue;
                            }

                            if (rstQueryItem.tokenList[i].strTerm != pair.Value[j].tokenList[i].strTerm)
                            {
                                Console.WriteLine("Query with different clicked url is inconsistent");
                                bIgnore = true;

                                foreach (QueryItem qi in pair.Value)
                                {
                                    sw_log.WriteLine(qi.strQuery);
                                    StringBuilder sb = new StringBuilder();
                                    foreach (Token tkn in qi.tokenList)
                                    {
                                        sb.Append(tkn.strTerm);
                                        sb.Append(" ");
                                    }
                                    sw_log.WriteLine(sb.ToString().Trim());
                                }
                                sw_log.WriteLine();



                                break;
                            }
                            rstQueryItem.tokenList[i].fWeight += (((double)pair.Value[j].freq) / ((double)iTotalFreq)) * pair.Value[j].tokenList[i].fWeight;
                        }

                        if (bIgnore == true)
                        {
                            break;
                        }
                    }
                    if (bIgnore == true)
                    {
                        continue;
                    }

                    string strOutput = pair.Key + "\t" + iTotalFreq.ToString() + "\t";
                    for (int i = 0; i < rstQueryItem.tokenList.Count; i++)
                    {
                        strOutput = strOutput + rstQueryItem.tokenList[i].strTerm + "[" + rstQueryItem.tokenList[i].fWeight.ToString("0.##") + "]\t";
                    }
                    sw.WriteLine(strOutput);
                }
            }
            sw_log.Close();
        }

        private static bool StatTermWeightInQuery(List<QueryItem> qiList)
        {
            List<QueryItem> tmp_qiList = new List<QueryItem>();
            foreach (QueryItem item in qiList)
            {
                wordseg.Segment(item.strQuery, tokens, false);
                List<Token> tknList = new List<Token>();
                bool bIgnored = false;
                for (int i = 0;i < tokens.tokenList.Count;i++)
                {
                    WordSeg.Token wbTkn = tokens.tokenList[i];
                    if (wbTkn.strTerm.Trim().Length > 0)
                    {
                        //ignore empty string
                        Token token = new Token();
                        token.strTerm = wbTkn.strTerm;

                        //check duplicate terms in a query
                        //If duplicated terms is found, drop current query
                        for (int j = 0; j < tknList.Count; j++)
                        {
                            if (tknList[j].strTerm == token.strTerm)
                            {
                                //found it
                                bIgnored = true;
                                break;
                            }
                        }

                        if (bIgnored == true)
                        {
                            break;
                        }

                        tknList.Add(token);
                    }
                }

                if (bIgnored == false)
                {
                    item.tokenList = tknList;
                    tmp_qiList.Add(item);
                }
            }

            qiList.Clear();

            bool bEntireCluster = false;
            for (int i = 0;i < tmp_qiList.Count;i++)
            {
                Dictionary<Token, int> termHash2Freq = new Dictionary<Token, int>();
                int totalFreq = tmp_qiList[i].freq;
                int queryInCluster = 0;
                foreach (Token item in tmp_qiList[i].tokenList)
                {
                    termHash2Freq.Add(item, tmp_qiList[i].freq);
                }

                //Check all qiList[i]'s sub-query and statistic frequency
                for (int j = 0; j < tmp_qiList.Count; j++)
                {
                    if (i != j)
                    {
                        List<Token> joinBList = new List<Token>();
                        if (AsubOfB(tmp_qiList[j].tokenList, tmp_qiList[i].tokenList, joinBList) == true)
                        {
                            queryInCluster++;
                            totalFreq += tmp_qiList[j].freq;
                            foreach (Token item in joinBList)
                            {
                                termHash2Freq[item] += tmp_qiList[j].freq;
                            }
                        }
                        else if (AsubOfB_2(tmp_qiList[i].tokenList, tmp_qiList[j].tokenList, joinBList) == true)
                        {
                            queryInCluster++;
                            totalFreq += tmp_qiList[j].freq;
                            foreach (Token item in joinBList)
                            {
                                termHash2Freq[item] += tmp_qiList[j].freq;
                            }
                        }
                    }
                }

                if (queryInCluster < MIN_CLUSTER_SIZE)
                {
                    continue;
                }

                if (queryInCluster + 1 == tmp_qiList.Count && tmp_qiList[i].strQuery.Length >= 2)
                {
                    //All other queries are current query's sub or super set.
                    bEntireCluster = true;
                }
                
                foreach (Token item in tmp_qiList[i].tokenList)
                {
                    double fWeight = ((double)termHash2Freq[item]) / ((double)totalFreq);
                    item.fWeight = fWeight;
                }

                qiList.Add(tmp_qiList[i]);
            }

            return bEntireCluster;
        }
    }
}


