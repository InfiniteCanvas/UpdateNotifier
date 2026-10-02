using UpdateNotifier.Utilities;

namespace UpdateNotifier.Tests;

public sealed class StringUtilitiesTests
{
	[Fact]
	public void GetThumbnailUrl_DoubleQuotedSrc_ExtractsUrl()
	{
		const string html = """<p>Some text</p><img src="https://cdn.example.com/banner.png" alt="" />""";

		var found = html.GetThumbnailUrl(out var thumbnailUrl);

		Assert.True(found);
		Assert.Equal("https://cdn.example.com/banner.png", thumbnailUrl);
	}

	[Fact]
	public void GetThumbnailUrl_SingleQuotedSrc_ExtractsUrl()
	{
		const string html = """<img src='https://cdn.example.com/banner.png' />""";

		var found = html.GetThumbnailUrl(out var thumbnailUrl);

		Assert.True(found);
		Assert.Equal("https://cdn.example.com/banner.png", thumbnailUrl);
	}

	[Fact]
	public void GetThumbnailUrl_AttributesBeforeSrc_ExtractsUrl()
	{
		const string html = """<img class="banner" loading="lazy" width="600" src="https://cdn.example.com/banner.png" />""";

		var found = html.GetThumbnailUrl(out var thumbnailUrl);

		Assert.True(found);
		Assert.Equal("https://cdn.example.com/banner.png", thumbnailUrl);
	}

	[Fact]
	public void GetThumbnailUrl_NoImgTag_ReturnsFalseWithEmptyUrl()
	{
		const string html = """<p>Just a description, no banner here.</p><a href="https://cdn.example.com/page">link</a>""";

		var found = html.GetThumbnailUrl(out var thumbnailUrl);

		Assert.False(found);
		Assert.Equal(string.Empty, thumbnailUrl);
	}

	[Fact]
	public void GetThumbnailUrl_NonHttpScheme_ReturnsFalse()
	{
		var dataUri = """<img src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUg==" alt="embedded" />""";
		var scriptUrl = """<img src="javascript:alert(1)" />""";

		Assert.False(dataUri.GetThumbnailUrl(out var dataThumbnail));
		Assert.Equal(string.Empty, dataThumbnail);

		Assert.False(scriptUrl.GetThumbnailUrl(out var scriptThumbnail));
		Assert.Equal(string.Empty, scriptThumbnail);
	}

	[Fact]
	public void GetThumbnailUrl_HtmlEntityInUrl_DecodesEntity()
	{
		const string html = """<img src="https://x/a.jpg?w=1&amp;h=2" />""";

		var found = html.GetThumbnailUrl(out var thumbnailUrl);

		Assert.True(found);
		Assert.Equal("https://x/a.jpg?w=1&h=2", thumbnailUrl);
	}

	[Fact]
	public void GetThumbnailUrl_NullHtml_ReturnsFalseWithEmptyUrl()
	{
		string? html = null;

		var found = html.GetThumbnailUrl(out var thumbnailUrl);

		Assert.False(found);
		Assert.Equal(string.Empty, thumbnailUrl);
	}
}
