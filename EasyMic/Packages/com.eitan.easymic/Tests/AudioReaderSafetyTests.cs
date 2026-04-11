using System;
using System.IO;
using System.Reflection;
using Eitan.EasyMic.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Eitan.EasyMic.Tests
{
    public class AudioReaderSafetyTests
    {
        [Test]
        public void WorkerLoop_DoesNotUseLocalloc()
        {
            var workerLoop = typeof(AudioReader).GetMethod("WorkerLoop", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(workerLoop, Is.Not.Null);

            var body = workerLoop.GetMethodBody();
            Assert.That(body, Is.Not.Null);

            var il = body.GetILAsByteArray();
            Assert.That(il, Is.Not.Null.And.Not.Empty);

            for (int i = 0; i < il.Length - 1; i++)
            {
                bool isLocalloc = il[i] == 0xFE && il[i + 1] == 0x0F;
                Assert.That(isLocalloc, Is.False, "AudioReader.WorkerLoop must not use stackalloc/localloc inside its long-lived loop.");
            }
        }

        [Test]
        public void AudioReaderDerivedDisposers_CallBaseDispose()
        {
            string sourceRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages/com.eitan.easymic", "Runtime"));
            Assert.That(Directory.Exists(sourceRoot), Is.True, $"Runtime source root was not found: {sourceRoot}");

            var files = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var source = File.ReadAllText(file);
                if (!source.Contains(": AudioReader", StringComparison.Ordinal) ||
                    !source.Contains("override void Dispose()", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.That(
                    source.Contains("base.Dispose();", StringComparison.Ordinal),
                    Is.True,
                    $"AudioReader subclass disposer must call base.Dispose(): {file}");
            }
        }
    }
}
