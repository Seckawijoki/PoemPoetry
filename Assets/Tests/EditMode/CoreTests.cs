using System.Collections.Generic;
using NUnit.Framework;
using PoemPoetry.Data;
using PoemPoetry.Services;

namespace PoemPoetry.Tests
{
    /// <summary>
    /// In-Unity smoke tests for the pure logic. The full suite (64 checks incl. the content
    /// pipeline and repository round-trips) lives in Tools/TestHarness and runs off-engine.
    /// </summary>
    public class CoreTests
    {
        [Test]
        public void Rhyme_FinalsAndGroups()
        {
            Assert.AreEqual("iang", PinyinRhyme.Final("xiang1"));
            Assert.AreEqual("uang", PinyinRhyme.Final("guang1"));
            Assert.AreEqual("i_buzz", PinyinRhyme.Final("zhi1"));
            Assert.AreEqual("ian", PinyinRhyme.Final("jian1"));
            Assert.AreEqual("10", PinyinRhyme.GroupOf("uang"));
            Assert.AreEqual("8", PinyinRhyme.GroupOf("ian"));
            Assert.AreEqual("13", PinyinRhyme.GroupOf("i_buzz"));
        }

        [Test]
        public void Score_AccuracyAndStreak()
        {
            Assert.AreEqual(70, ScoreMath.AccuracyPercent(7, 10));
            Assert.AreEqual(0, ScoreMath.AccuracyPercent(0, 0));
            Assert.AreEqual(100, ScoreMath.AccuracyPercent(5, 5));
            Assert.AreEqual(3, ScoreMath.BestStreak(new[] { true, true, false, true, true, true, false }));
        }

        [Test]
        public void Generator_RespectsHardConstraints()
        {
            // Two poems sharing char-count + rhyme group so distractors can be found.
            var a = MakePoem("p-a", new[] { "秦时明月汉时关", "万里长征人未还" });
            var b = MakePoem("p-b", new[] { "黄河远上白云间", "春风不度玉门关" });
            var poems = new List<Poem> { a, b };

            var gen = new QuestionGenerator(poems, new SystemRandomSource(1));
            var q = gen.Generate(a, 0, 2);
            Assert.IsNotNull(q, "expected a question for a line with available distractors");
            Assert.AreEqual(2, q.Distractors.Count);
            foreach (var d in q.Distractors)
            {
                Assert.AreEqual(a.Lines[0].CharCount, d.CharCount, "same 字数");
                Assert.AreEqual(a.Lines[0].RhymeGroup, d.RhymeGroup, "same 韵组");
                Assert.AreNotEqual(a.Id, d.SourcePoemId, "distractor from a different poem");
            }
        }

        private static Poem MakePoem(string id, string[] lines)
        {
            var p = new Poem { Id = id, Title = id, Author = "x", Dynasty = "唐", Fame = "famous" };
            foreach (var text in lines)
            {
                // group 8 (ian/uan/an), 7 chars — matches the sample lines above.
                p.Lines.Add(new PoemLine
                {
                    Text = text,
                    CharCount = 7,
                    LastChar = text.Substring(text.Length - 1),
                    RhymeFinal = "ian",
                    RhymeGroup = "8",
                    IsRhymeLine = true,
                });
            }
            return p;
        }
    }
}
