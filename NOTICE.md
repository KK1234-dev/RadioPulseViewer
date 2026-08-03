# Notices for RadioPulseViewer

## Repository license

The source archive from which this repository was prepared did not contain a
license or copyright notice. At the repository publisher's direction, the
original RadioPulseViewer source code and the documentation added for this
repository are released under the [MIT License](LICENSE), with:

```text
Copyright (c) 2025 Keisuke Katahira
```

The MIT License does **not** grant rights in third-party data, services,
software, names, logos, or other material described below.

## Program and station data

[`RadioPulseViewer/Data/programs.json`](RadioPulseViewer/Data/programs.json)
contains station names, program metadata, descriptions, hashtags, and links
associated with broadcasters and external services. That file is distributed
as reference/fallback data and is excluded from the MIT License. Rights in its
contents remain with their respective rightsholders. The original archive did
not include evidence establishing a separate redistribution license for this
data; users are responsible for confirming that their intended use is
permitted and for replacing or removing the data when necessary.

## Microsoft WebView2

The project references `Microsoft.Web.WebView2` version `1.0.4078.44` through
NuGet. The package and the Microsoft Edge WebView2 Runtime are Microsoft
components provided under their own terms; they are not relicensed by this
repository.

- [Microsoft.Web.WebView2 1.0.4078.44 package](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4078.44)
- [Package license](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4078.44/License)
- [WebView2 Runtime distribution guidance](https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution)

No WebView2 binaries or runtime files are committed to this repository; they
are obtained separately during package restore or runtime installation.

## X API

The optional official Post-count feature calls X API v2 and requires credentials,
permissions, credits, and pricing terms supplied by X Corp. The repository does
not include a Bearer Token or grant X API access. Users must review the current
X Developer Agreement, API documentation, and pricing before enabling the
feature.

- [X Developer Agreement and Policy](https://developer.x.com/en/developer-terms/agreement-and-policy)
- [X API Post Counts](https://docs.x.com/x-api/posts/counts/introduction)
- [X API Pricing](https://docs.x.com/x-api/getting-started/pricing)

## Public graph recording

The public graph recording feature does not automatically extract data from a
web page. It opens a public search page for reference and stores only values
that the user manually enters. The entered values remain subject to the source
service's terms, accuracy, availability, and permitted-use conditions.

## External services and content

RadioPulseViewer accesses or links to radiko, Yahoo! JAPAN real-time search,
X API, and broadcaster websites. Those services, their program information,
and content displayed in WebView2 are governed by the respective providers'
terms, privacy policies, availability, pricing, and technical specifications.
They are not covered by the MIT License.

- [radiko terms and policies](https://radiko.jp/rg/policy/dark-mode/)
- [LY Corporation terms](https://www.lycorp.co.jp/ja/company/terms/)
- [LY Corporation privacy policy](https://privacy.lycorp.co.jp/ja/)

RadioPulseViewer and KK1234-dev are not affiliated with, sponsored by, or
endorsed by Microsoft, radiko, LY Corporation, Yahoo! JAPAN, X Corp., or any
listed broadcaster. Company, service, station, and program names may be
trademarks or other protected identifiers of their respective owners.
