﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Globbing;
using Microsoft.Build.Shared;
using Xunit;

namespace Microsoft.Build.Engine.UnitTests.Globbing
{
    /// <summary>
    ///     Actual parsing tests are covered by FileMatcher_Tests (e.g. FileMatcher_Tests.SplitFileSpec)/>
    /// </summary>
    public class MSBuildGlob_Tests
    {
        [Theory]
        [InlineData("")]
        [InlineData("a")]
#if TEST_ISWINDOWS
        [InlineData(@"c:\a")]
#else
        [InlineData("/a")]
#endif
        public void GlobRootEndsWithTrailingSlash(string globRoot)
        {
            var glob = MSBuildGlob.Parse(globRoot, "*");

            Assert.Equal(glob._globRoot.LastOrDefault(), Path.DirectorySeparatorChar);
        }

        [Fact]
        public void GlobFromAbsoluteRootDoesNotChangeTheRoot()
        {
            var globRoot = NativeMethodsShared.IsWindows ? @"c:\a" : "/a";
            var glob = MSBuildGlob.Parse(globRoot, "*");

            var expectedRoot = Path.Combine(Directory.GetCurrentDirectory(), globRoot).WithTrailingSlash();
            Assert.Equal(expectedRoot, glob._globRoot);
        }

        [Fact]
        public void GlobFromEmptyRootUsesCurrentDirectory()
        {
            var glob = MSBuildGlob.Parse(string.Empty, "*");

            Assert.Equal(Directory.GetCurrentDirectory().WithTrailingSlash(), glob._globRoot);
        }

        [Fact]
        public void GlobFromNullRootThrows()
        {
            Assert.Throws<ArgumentNullException>(() => MSBuildGlob.Parse(null, "*"));
        }

        [Fact]
        public void GlobFromRelativeGlobRootNormalizesRootAgainstCurrentDirectory()
        {
            var globRoot = "a/b/c";
            var glob = MSBuildGlob.Parse(globRoot, "*");

            var expectedRoot = NormalizeRelativePathForGlobRepresentation(globRoot);
            Assert.Equal(expectedRoot, glob._globRoot);
        }

        [Fact]
        public void GlobFromRootWithInvalidPathThrows()
        {
            foreach (var invalidPathChar in FileUtilities.InvalidPathChars)
            {
                Assert.Throws<ArgumentException>(() => MSBuildGlob.Parse(invalidPathChar.ToString(), "*"));
            }
        }

        [Theory]
        [InlineData(
            "a/b/c",
            "**",
            "a/b/c"
            )]
        [InlineData(
            "a/b/c",
            "../../**",
            "a"
            )]
        [InlineData(
            "a/b/c",
            "../d/e/**",
            "a/b/d/e"
            )]
        public void GlobWithRelativeFixedDirectoryPartShouldMissmatchTheGlobRoot(string globRoot, string filespec, string expectedFixedDirectoryPart)
        {
            var glob = MSBuildGlob.Parse(globRoot, filespec);

            var expectedFixedDirectory = NormalizeRelativePathForGlobRepresentation(expectedFixedDirectoryPart);

            Assert.Equal(expectedFixedDirectory, glob.FixedDirectoryPart);
        }

        [Fact]
        public void GlobFromNullFileSpecThrows()
        {
            Assert.Throws<ArgumentNullException>(() => MSBuildGlob.Parse(null));
        }

        // Smoke test. Comprehensive parsing tests in FileMatcher

        [Fact]
        public void GlobParsingShouldInitializeState()
        {
            var globRoot = NativeMethodsShared.IsWindows ? @"c:\a" : "/a";
            var fileSpec = $"b/**/*.cs";
            var glob = MSBuildGlob.Parse(globRoot, fileSpec);

            var expectedFixedDirectory = Path.Combine(globRoot, "b").WithTrailingSlash();

            Assert.True(glob.IsLegal);
            Assert.Equal(true, glob._needsRecursion);
            Assert.Equal(fileSpec, glob._fileSpec);

            Assert.Equal(globRoot.WithTrailingSlash(), glob._globRoot);
            Assert.True(glob.FixedDirectoryPart.StartsWith(glob._globRoot));

            Assert.Equal(expectedFixedDirectory, glob.FixedDirectoryPart);
            Assert.Equal("**/", glob.WildcardDirectoryPart);
            Assert.Equal("*.cs", glob.FilenamePart);
        }

        [Fact]
        public void GlobParsingShouldInitializePartialStateOnIllegalFileSpec()
        {
            var globRoot = NativeMethodsShared.IsWindows ? @"c:\a" : "/a";
            var illegalFileSpec = $"b/.../**/*.cs";
            var glob = MSBuildGlob.Parse(globRoot, illegalFileSpec);

            Assert.False(glob.IsLegal);
            Assert.Equal(false, glob._needsRecursion);
            Assert.Equal(illegalFileSpec, glob._fileSpec);

            Assert.Equal(globRoot.WithTrailingSlash(), glob._globRoot);

            Assert.Equal(string.Empty, glob.FixedDirectoryPart);
            Assert.Equal(string.Empty, glob.WildcardDirectoryPart);
            Assert.Equal(string.Empty, glob.FilenamePart);
        }

        [Fact]
        public void GlobIsNotUnescaped()
        {
            var glob = MSBuildGlob.Parse("%42/%42");

            Assert.True(glob.IsLegal);
            Assert.True(glob.FixedDirectoryPart.EndsWith("%42" + Path.DirectorySeparatorChar));
            Assert.Equal(string.Empty, glob.WildcardDirectoryPart);
            Assert.Equal("%42", glob.FilenamePart);
        }

        [Fact]
        public void GlobMatchingShouldThrowOnNullMatchArgument()
        {
            var glob = MSBuildGlob.Parse("*");

            Assert.Throws<ArgumentNullException>(() => glob.IsMatch(null));
        }

        [Fact]
        public void GlobMatchShouldReturnFalseIfArgumentContainsInvalidPathOrFileCharacters()
        {
            var glob = MSBuildGlob.Parse("*");

            foreach (var invalidPathChar in FileUtilities.InvalidPathChars)
            {
                Assert.False(glob.IsMatch(invalidPathChar.ToString()));
            }

            foreach (var invalidFileChar in FileUtilities.InvalidFileNameChars)
            {
                if (invalidFileChar == '\\' || invalidFileChar == '/')
                {
                    continue;
                }

                Assert.False(glob.IsMatch(invalidFileChar.ToString()));
            }
        }

        [Fact]
        public void GlobMatchingBehaviourWhenInputIsASingleSlash()
        {
            var glob = MSBuildGlob.Parse("*");

            if (NativeMethodsShared.IsUnixLike)
            {
                // \ is an acceptable file character on Unix, * should match it
                Assert.True(glob.IsMatch("\\"));

                // / means root on Unix
                Assert.False(glob.IsMatch("/"));
            }
            else
            {
                // \ means partition root on Windows
                Assert.False(glob.IsMatch("\\"));

                // / also means partition root on Windows
                Assert.False(glob.IsMatch("/"));
            }
        }

        [Fact]
        public void GlobMatchingShouldWorkWithLiteralStrings()
        {
            var glob = MSBuildGlob.Parse("abc");

            Assert.True(glob.IsMatch("abc"));
        }

        // Just a smoke test. Comprehensive tests in FileMatcher_Tests

        [Fact]
        public void GlobMatchingShouldWorkWithWildcards()
        {
            var glob = MSBuildGlob.Parse("ab?c*");

            Assert.False(glob.IsMatch("acd"));
        }

        [Fact]
        public void GlobMatchingShouldNotUnescapeInput()
        {
            var glob = MSBuildGlob.Parse("%42");

            Assert.False(glob.IsMatch("B"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("a/b/c")]
        public void GlobShouldMatchEmptyArgWhenGlobIsEmpty(string globRoot)
        {
            var glob = MSBuildGlob.Parse(globRoot, string.Empty);

            Assert.True(glob.IsMatch(string.Empty));
        }

        [Theory]
        [InlineData("")]
        [InlineData("a/b/c")]
        public void GlobShouldMatchEmptyArgWhenGlobAcceptsEmptyString(string globRoot)
        {
            var glob = MSBuildGlob.Parse(globRoot, "*");

            Assert.True(glob.IsMatch(string.Empty));
        }

        [Theory]
        [InlineData("")]
        [InlineData("a/b/c")]
        public void GlobShouldNotMatchEmptyArgWhenGlobDoesNotRepresentEmpty(string globRoot)
        {
            var glob = MSBuildGlob.Parse(globRoot, "*a*");

            Assert.False(glob.IsMatch(string.Empty));
        }

        [Fact(Skip = "TODO")]
        public void GlobMatchingShouldTreatIllegalFileSpecAsLiteral()
        {
            var illegalSpec = "|...*";
            var glob = MSBuildGlob.Parse(illegalSpec);

            Assert.False(glob.IsLegal);
            Assert.True(glob.IsMatch(illegalSpec));
        }

        public static IEnumerable<object[]> GlobMatchingShouldRespectTheRootOfTheGlobTestData => GlobbingTestData.GlobbingConesTestData;

        [Theory]

        [MemberData(nameof(GlobMatchingShouldRespectTheRootOfTheGlobTestData))]

        public void GlobMatchingShouldRespectTheRootOfTheGlob(string fileSpec, string stringToMatch, string globRoot, bool shouldMatch)
        {
            var glob = MSBuildGlob.Parse(globRoot, fileSpec);

            if (shouldMatch)
            {
                Assert.True(glob.IsMatch(stringToMatch));
            }
            else
            {
                Assert.False(glob.IsMatch(stringToMatch));
            }
        }

        private static string NormalizeRelativePathForGlobRepresentation(string expectedFixedDirectoryPart)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), expectedFixedDirectoryPart).Replace("/", "\\").WithTrailingSlash();
        }

        [Fact]
        public void GlobMatchingShouldWorkWithComplexRelativeLiterals()
        {
            var glob = MSBuildGlob.Parse("u/x", "../../u/x/d11/d21/../d22/../../d12/a.cs");

            Assert.True(glob.IsMatch(@"../x/d13/../../x/d12/d23/../a.cs"));
            Assert.False(glob.IsMatch(@"../x/d13/../x/d12/d23/../a.cs"));
        }

        [Theory]
        [InlineData(
            @"a/b\c",
            @"d/e\f/**\a.cs",
            @"d\e/f\g/h\i/a.cs")]
        [InlineData(
            @"a/b\c",
            @"d/e\f/*b*\*.cs",
            @"d\e/f\abc/a.cs")]
        [InlineData(
            @"a/b/\c",
            @"d/e\/*b*/\*.cs",
            @"d\e\\abc/\a.cs")]
        public void GlobMatchingIgnoresSlashOrientationAndRepetitions(string globRoot, string fileSpec, string stringToMatch)
        {
            var glob = MSBuildGlob.Parse(globRoot, fileSpec);

            Assert.True(glob.IsMatch(stringToMatch));
        }
    }
}