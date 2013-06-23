﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace QueryTermWeightAnalyzer.Features
{
    class BigramInLeftFeature : IFeature
    {
        public string GetName()
        {
            return "BigramInLeft";
        }

        public string GetValue(FeatureContext context)
        {
            if (context.index > 0)
            {
                string strTerm1 = context.tknList[context.index - 1].strTerm;
                string strTerm2 = context.tknList[context.index].strTerm;
                string strBigram = strTerm1 + " " + strTerm2;
                if (context.bigramDict.ContainsKey(strBigram) == true)
                {
                    return context.bigramDict[strBigram].freq.ToString();
                }
            }
            return "0";
        }
    }
}
