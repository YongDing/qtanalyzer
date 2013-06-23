﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using AdvUtils;
using WordSeg;
using CRFSharp;
using CRFSharpWrapper;
using QueryTermWeightAnalyzer.Features;
using StochasticGradientBoost;

namespace QueryTermWeightAnalyzer
{
    public class Token
    {
        public string strTerm;
        public int rankId;
        public float rankingscore;
    }

    public class Bigram
    {
        public long freq;
        public double mi;
    }

    public class Unigram
    {
        public long freq;
        public double idf;
        public double pRank0;
        public double pRank1;
        public double pRank2;
        public double pRank3;
        public double pRank4;
    }

    public class QueryTermWeightAnalyzer
    {
        const string KEY_LEXICAL_DICT_FILE_NAME = "LexicalDictFileName";
        const string KEY_MODEL_FILE_NAME = "ModelFileName";
        const string KEY_UNIGRAM_FILE_NAME = "UnigramFileName";
        const string KEY_BIGRAM_FILE_NAME = "BigramFileName";
        const string KEY_RANKPERCENT_FILE_NAME = "RankPercentFileName";
        const string KEY_RANKMODEL_FILE_NAME = "RankingModelFileName";
        const string KEY_ACTIVEFEATURE_FILE_NAME = "ActiveFeatureFileName";
        const string KEY_PUNCT_DICT_FILE_NAME = "PunctDictFileName";
        
        WordSeg.WordSeg wordseg;
        WordSeg.Tokens wbTokens;

        public crf_out crf_out;
        public SegDecoderTagger crf_tag;
        public string[,] inbuf;

        CRFSharpWrapper.Decoder crf;
        ModelFeatureGenerator featureGenerator;

        BigDictionary<string, Bigram> bigramDict;
        BigDictionary<string, Unigram> unigramDict;
        HashSet<string> setPunct;

        double maxIDF = 0.0;

        List<IFeature> featureList;
        NNModel modelMSN;


        private void LoadPunctDict(string strFileName)
        {
            setPunct = new HashSet<string>(File.ReadAllLines(strFileName));
            if (setPunct == null)
            {
                Console.WriteLine("Failed to load punct dictionary from {0}", strFileName);
            }
        }

        private bool LoadRankModel(string strModelFileName, string strFtrFileName)
        {
            if (strModelFileName == null || strModelFileName.Length == 0)
            {
                return false;
            }
            if (strFtrFileName == null || strFtrFileName.Length == 0)
            {
                return false;
            }

            modelMSN = NNModel.Create(strModelFileName);
            if (modelMSN == null)
            {
                Console.WriteLine("Failed to load model: " + strModelFileName);
                return false;
            }

            string[] activeFeatureNames = null;
            //read and process only a subset of activated features as specified in the activeFeatureFile
            activeFeatureNames = File.ReadAllLines(strFtrFileName);
            if (modelMSN.SetFeatureNames(activeFeatureNames) == false)
            {
                Console.WriteLine("Failed to set feature set");
                return false;
            }

            return true;
        }

        private void LoadRankPercent(string strFileName)
        {
            StreamReader sr = new StreamReader(strFileName);
            while (sr.EndOfStream == false)
            {
                string strLine = sr.ReadLine();
                string[] items = strLine.Split('\t');

                if (unigramDict.ContainsKey(items[0]) == true)
                {
                    if (items[1] == "NaN")
                    {
                        items[1] = "0.0";
                    }
                    if (items[2] == "NaN")
                    {
                        items[2] = "0.0";
                    }
                    if (items[3] == "NaN")
                    {
                        items[3] = "0.0";
                    }
                    if (items[4] == "NaN")
                    {
                        items[4] = "0.0";
                    }
                    if (items[5] == "NaN")
                    {
                        items[5] = "0.0";
                    }

                    unigramDict[items[0]].pRank0 = double.Parse(items[1]);
                    unigramDict[items[0]].pRank1 = double.Parse(items[2]);
                    unigramDict[items[0]].pRank2 = double.Parse(items[3]);
                    unigramDict[items[0]].pRank3 = double.Parse(items[4]);
                    unigramDict[items[0]].pRank4 = double.Parse(items[5]);
                }
            }
            sr.Close();
        }

        private List<IFeature> InitFeatureList()
        {
            List<IFeature> featureList = new List<IFeature>();
            featureList.Add(new WordFormationFeature());
            featureList.Add(new TermLengthFeature());
            featureList.Add(new TermOffsetFeature());
            featureList.Add(new IsBeginTermFeature());
            featureList.Add(new IsEndTermFeature());
            featureList.Add(new UnigramTFFeature());
            featureList.Add(new UnigramIDFFeature());
            featureList.Add(new BigramInLeftFeature());
            featureList.Add(new BigramInRightFeature());
            featureList.Add(new PMIInLeftFeature());
            featureList.Add(new PMIInRightFeature());
            featureList.Add(new TermRank0PercentFeature());
            featureList.Add(new TermRank1PercentFeature());
            featureList.Add(new TermRank2PercentFeature());
            featureList.Add(new TermRank3PercentFeature());
            featureList.Add(new TermRank4PercentFeature());
            featureList.Add(new IsPunctFeature());

            return featureList;
        }

        private bool LoadUnigram(string strFileName)
        {
            unigramDict = new BigDictionary<string, Unigram>();
            StreamReader sr = new StreamReader(strFileName, Encoding.UTF8);
            string strLine = null;
            while ((strLine = sr.ReadLine()) != null)
            {
                string[] items = strLine.Split('\t');

                Unigram unigram = new Unigram();
                unigram.freq = long.Parse(items[1]);
                unigram.idf = double.Parse(items[2]);
                unigram.pRank0 = -1;
                unigram.pRank1 = -1;
                unigram.pRank2 = -1;
                unigram.pRank3 = -1;
                unigram.pRank4 = -1;

                if (unigramDict.ContainsKey(items[0]) == false)
                {
                    unigramDict.Add(items[0], unigram);
                }
                else
                {
                    Console.WriteLine("{0} is duplicated. [existed value:{1} new value:{2}]", items[0], unigramDict[items[0]].freq, unigram.freq);
                }

                if (unigram.idf > maxIDF)
                {
                    maxIDF = unigram.idf;
                }
            }
            sr.Close();

            return true;
        }

        private bool LoadBigram(string strFileName)
        {
            bigramDict = new BigDictionary<string, Bigram>();
            StreamReader sr = new StreamReader(strFileName, Encoding.UTF8);
            string strLine = null;
            while ((strLine = sr.ReadLine()) != null)
            {
                string[] items = strLine.Split('\t');
                Bigram bigram = new Bigram();
                bigram.freq = long.Parse(items[1]);
                bigram.mi = double.Parse(items[2]);

                if (bigramDict.ContainsKey(items[0]) == false)
                {
                    bigramDict.Add(items[0], bigram);
                }
                else
                {
                    Console.WriteLine("{0} is duplicated. [existed value:{1} new value:{2}]", items[0], bigramDict[items[0]].freq, bigram.freq);
                }
            }
            sr.Close();

            return true;
        }


        private Dictionary<string, string> LoadConfFile(string strConfFileName)
        {
            Dictionary<string, string> dict = new Dictionary<string, string>();
            StreamReader sr = new StreamReader(strConfFileName);
            while (sr.EndOfStream == false)
            {
                string strLine = sr.ReadLine();
                string[] items = strLine.Split('=');

                items[0] = items[0].ToLower().Trim();
                items[1] = items[1].ToLower().Trim();

                if (items[0] != KEY_LEXICAL_DICT_FILE_NAME.ToLower() &&
                    items[0] != KEY_MODEL_FILE_NAME.ToLower() &&
                    items[0] != KEY_UNIGRAM_FILE_NAME.ToLower() &&
                    items[0] != KEY_BIGRAM_FILE_NAME.ToLower() &&
                    items[0] != KEY_RANKPERCENT_FILE_NAME.ToLower() &&
                    items[0] != KEY_RANKMODEL_FILE_NAME.ToLower() &&
                    items[0] != KEY_ACTIVEFEATURE_FILE_NAME.ToLower() &&
                    items[0] != KEY_PUNCT_DICT_FILE_NAME.ToLower())
                {
                    throw new Exception("Invalidated configuration item");

                }
                dict.Add(items[0], items[1]);
            }

            sr.Close();

            return dict;
        }


        public bool Initialize(string strConfFileName)
        {
            //Load configuration file
            Dictionary<string, string> confDict;
            confDict = LoadConfFile(strConfFileName);

            if (confDict.ContainsKey(KEY_LEXICAL_DICT_FILE_NAME.ToLower()) == false)
            {
                Console.WriteLine("Failed to find key {0}", KEY_LEXICAL_DICT_FILE_NAME);
                return false;
            }

            if (confDict.ContainsKey(KEY_MODEL_FILE_NAME.ToLower()) == false)
            {
                Console.WriteLine("Failed to find key {0}", KEY_MODEL_FILE_NAME);
                return false;
            }

            //Load CRF model for word formation
            crf = new CRFSharpWrapper.Decoder();
            string strModelFileName = confDict[KEY_MODEL_FILE_NAME.ToLower()];
            crf.LoadModel(strModelFileName);
            featureGenerator = new ModelFeatureGenerator();
            crf_out = new CRFSharpWrapper.crf_out();
            crf_tag = crf.CreateTagger();
            inbuf = null;

            wordseg = new WordSeg.WordSeg();
            //Load lexical dictionary
            wordseg.LoadLexicalDict(confDict[KEY_LEXICAL_DICT_FILE_NAME.ToLower()], true);
            //Initialize word breaker's token instance
            wbTokens = wordseg.CreateTokens(1024);

            //Load punct dict
            if (confDict.ContainsKey(KEY_PUNCT_DICT_FILE_NAME.ToLower()) == true)
            {
                LoadPunctDict(confDict[KEY_PUNCT_DICT_FILE_NAME.ToLower()]);
            }
            else
            {
                Console.WriteLine("Failed to find key {0}", KEY_PUNCT_DICT_FILE_NAME);
                return false;
            }

            //Load language model
            if (confDict.ContainsKey(KEY_UNIGRAM_FILE_NAME.ToLower()) == true)
            {
                if (LoadUnigram(confDict[KEY_UNIGRAM_FILE_NAME.ToLower()]) == false)
                {
                    return false;
                }
            }
            else
            {
                Console.WriteLine("Failed to find key {0}", KEY_UNIGRAM_FILE_NAME);
                return false;
            }

            if (confDict.ContainsKey(KEY_BIGRAM_FILE_NAME.ToLower()) == true)
            {
                if (LoadBigram(confDict[KEY_BIGRAM_FILE_NAME.ToLower()]) == false)
                {
                    return false;
                }
            }
            else
            {
                Console.WriteLine("Failed to find key {0}", KEY_BIGRAM_FILE_NAME);
                return false;
            }

            if (confDict.ContainsKey(KEY_RANKPERCENT_FILE_NAME.ToLower()) == true)
            {
                LoadRankPercent(confDict[KEY_RANKPERCENT_FILE_NAME.ToLower()]);
            }
            else
            {
                Console.WriteLine("Failed to find key {0}", KEY_RANKPERCENT_FILE_NAME);
                return false;
            }


            //Initialize feature set for ranking
            featureList = InitFeatureList();

            if (confDict.ContainsKey(KEY_RANKMODEL_FILE_NAME.ToLower()) == false)
            {
                Console.WriteLine("Failed to find key {0}", KEY_RANKMODEL_FILE_NAME);
                return false;
            }

            if (confDict.ContainsKey(KEY_ACTIVEFEATURE_FILE_NAME.ToLower()) == false)
            {
                Console.WriteLine("Failed to find key {0}", KEY_ACTIVEFEATURE_FILE_NAME);
                return false;
            }

            //Load ranking model
            LoadRankModel(confDict[KEY_RANKMODEL_FILE_NAME.ToLower()], confDict[KEY_ACTIVEFEATURE_FILE_NAME.ToLower()]);

            return true;

        }

      

        private string GetTermRankTag(List<string> strTagList)
        {
            foreach (string item in strTagList)
            {
                if (item.StartsWith("RANK_") == true)
                {
                    return item;
                }
            }
            return null;
        }

        private List<string> ExtractCoreToken(List<Token> tknList)
        {
            StringBuilder sb = new StringBuilder();
            List<string> termList = new List<string>();
            foreach (Token tkn in tknList)
            {
                if (tkn.rankId < 2)
                {
                    termList.Add(tkn.strTerm);
                }
            }
            return termList;
        }

        private bool HasOptTerms(List<Token> tknList)
        {
            foreach (Token tkn in tknList)
            {
                if (tkn.rankId >= 2)
                {
                    return true;
                }
            }
            return false;
        }

        private void NormalizeTermWeight(List<Token> tknList)
        {
            //Check the number of token with rank0, if there is no such token, 
            //Adjust token's rank id
            int rank0Cnt = 0, rank1Cnt = 0;
            foreach (Token tkn in tknList)
            {
                if (tkn.rankId == 0)
                {
                    rank0Cnt++;
                }
                else if (tkn.rankId == 1)
                {
                    rank1Cnt++;
                }
            }
            if (rank0Cnt == 0 && rank1Cnt == 0)
            {
                //If all token's term rank are rank2, rewrite them as rank0
                foreach (Token tkn in tknList)
                {
                    tkn.rankId = 0;
                }
            }
            else if (rank0Cnt == 0)
            {
                //If no token's term ran is rank0, increase all token's term rank level
                foreach (Token tkn in tknList)
                {
                    tkn.rankId--;
                }
            }
        }

        private List<Token> LabelString(List<string> termList)
        {
            List<Token> tknList = new List<Token>();

            List<List<string>> sinbuf = featureGenerator.GenerateFeature(termList);
            inbuf = new string[sinbuf.Count, sinbuf[0].Count];
            for (int i = 0; i < sinbuf.Count; i++)
            {
                for (int j = 0; j < sinbuf[0].Count; j++)
                {
                    inbuf[i, j] = sinbuf[i][j];
                }
            }

            int ret = crf.Segment(crf_out, crf_tag, inbuf, 1, 0);
            crf_term_out item = crf_out.term_buf[0];
            if (item.Count != termList.Count)
            {
                throw new Exception("Failed to parse word formation by model");
            }

            for (int j = 0; j < item.Count; j++)
            {
                int offset = item.offsetList[j];
                int len = item.lengthList[j];
                string strNE = item.nePropList[j].strTag;

                Token token = new Token();
                token.strTerm = termList[j];
                token.rankId = int.Parse(item.nePropList[j].strTag.Substring(5));

                tknList.Add(token);
            }

            return tknList;
        }


        private List<Token> LabelString(string strText)
        {
            List<string> termList = new List<string>();
            wordseg.Segment(strText, wbTokens, false);
            for (int i = 0; i < wbTokens.tokenList.Count; i++)
            {
                if (wbTokens.tokenList[i].strTerm.Trim().Length == 0)
                {
                    continue;
                }
                termList.Add(wbTokens.tokenList[i].strTerm);
            }
            return LabelString(termList);
        }

        private bool MergeTermWeight(List<Token> subTknList, List<Token> tknList)
        {
            if (HasOptTerms(subTknList) == false)
            {
                //No need to merge
                return false;
            }

            //Check whether the token boundary between tknList and subTknList are the same.
            //If not, do not merge them
            int i = 0, j = 0;
            while (j < tknList.Count)
            {
                if (tknList[j].rankId >= 2)
                {
                    j++;
                    continue;
                }

                if (tknList[j].strTerm != subTknList[i].strTerm)
                {
                    return false;
                }
                i++;
                j++;
            }

            i = 0;
            j = 0;
            while (j < tknList.Count)
            {
                if (tknList[j].rankId >= 2)
                {
                    tknList[j].rankId++;
                    j++;
                    continue;
                }

                tknList[j].rankId = subTknList[i].rankId;
                i++;
                j++;
            }

            return true;

        }

        public List<Token> AnalyzeWordFormationLevel(List<string> termList)
        {
            List<Token> tknList = LabelString(termList);
            NormalizeTermWeight(tknList);

            if (HasOptTerms(tknList) == false)
            {
                //No optional term, return directly
                return tknList;
            }

            while (true)
            {
                List<string> coreTknList = ExtractCoreToken(tknList);
                List<Token> tmpTknList = LabelString(coreTknList);
                NormalizeTermWeight(tmpTknList);

                if (MergeTermWeight(tmpTknList, tknList) == false)
                {
                    //If no new result need to be mereged into result, end the process
                    break;
                }
            }

            return tknList;
        }


        public List<Token> AnalyzeWordFormationLevel(string strText)
        {
            List<Token> tknList = LabelString(strText);
            NormalizeTermWeight(tknList);

            if (HasOptTerms(tknList) == false)
            {
                //No optional term, return directly
                return tknList;
            }

            while (true)
            {
                List<string> coreTknList = ExtractCoreToken(tknList);
                List<Token> tmpTknList = LabelString(coreTknList);
                NormalizeTermWeight(tmpTknList);

                if (MergeTermWeight(tmpTknList, tknList) == false)
                {
                    //If no new result need to be mereged into result, end the process
                    break;
                }
            }

            return tknList;
        }

        public List<Token> Analyze(string strText)
        {
            strText = strText.ToLower().Trim();

            List<Token> tknList = AnalyzeWordFormationLevel(strText);
            FeatureContext context = new FeatureContext();
            context.bigramDict = bigramDict;
            context.tknList = tknList;
            context.unigramDict = unigramDict;
            context.setPunct = setPunct;
            context.maxIDF = maxIDF;

            for (int i = 0; i < tknList.Count; i++)
            {
                StringBuilder sb = new StringBuilder();
                context.index = i;
                List<float> ftrList = new List<float>();
                foreach (IFeature feature in featureList)
                {
                    string strValue = feature.GetValue(context);
                    ftrList.Add(float.Parse(strValue));
                }
                tknList[i].rankingscore = 1.0f - modelMSN.Evaluate(ftrList.ToArray());
            }

            return tknList;
        }

        #region Feature Extractor

        public string GetFeatureName()
        {
            StringBuilder sb = new StringBuilder();
            foreach (IFeature feature in featureList)
            {
                sb.Append(feature.GetName());
                sb.Append("\t");
            }
            return sb.ToString().Trim();
        }
        
        public List<string> ExtractFeature(List<string> termList)
        {
            List<Token> tknList = AnalyzeWordFormationLevel(termList);
            List<string> rstList = new List<string>();

            FeatureContext context = new FeatureContext();
            context.bigramDict = bigramDict;
            context.index = 0;
            context.tknList = tknList;
            context.unigramDict = unigramDict;
            context.setPunct = setPunct;
            context.maxIDF = maxIDF;

            for (int i = 0; i < tknList.Count; i++)
            {
                StringBuilder sb = new StringBuilder();
                context.index = i;
                foreach (IFeature feature in featureList)
                {
                    string strValue = feature.GetValue(context);
                    sb.Append(strValue);
                    sb.Append("\t");
                }

                rstList.Add(sb.ToString().Trim());
            }

            return rstList;
        }
        #endregion

    }
}
