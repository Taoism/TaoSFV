using Xunit;

namespace TaoSFV.Tests
{
	public class CacheTests
	{
		[Theory]
		[InlineData(@"D:\Music\Album", @"d:\music\album", @"d:\music\albun")]
		[InlineData(@"D:\Z", @"d:\z", @"d:\{")]           // lowercased before incrementing, as NOCASE compares
		[InlineData(@"\\NAS\Share\Ä", @"\\nas\share\Ä", @"\\nas\share\Å")] // non-ASCII isn't folded, like NOCASE
		public void PrefixRange(string prefix, string from, string to)
		{
			string actualFrom, actualTo;
			Cache.GetPrefixRange(prefix, out actualFrom, out actualTo);

			Assert.Equal(from, actualFrom);
			Assert.Equal(to, actualTo);
		}
	}
}
