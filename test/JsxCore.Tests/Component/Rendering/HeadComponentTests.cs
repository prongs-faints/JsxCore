using System.Text.RegularExpressions;
using JsxCore.Tests.Fixtures;
using Shouldly;

namespace JsxCore.Tests.Component.Rendering;

/// <summary>
/// <c>&lt;Head&gt;</c>, the next/head-shaped way of setting document head tags from inside a
/// component.
/// </summary>
public class HeadComponentTests
{
    private static JsxProjectFixture Project(string view)
    {
        var project = JsxProjectFixture.Create();
        project.AddView("Home/Index.tsx", view);
        return project;
    }

    [Fact]
    public async Task Head_OnAServerRender_LiftsItsChildrenIntoTheDocument()
    {
        using var project = Project("""
            "use server";
            import Head from "dotnet:rendering/head";

            export default function Index() {
                return (
                    <>
                        <Head>
                            <title>Products</title>
                            <meta name="description" content="Everything we sell" />
                        </Head>
                        <h1>Body</h1>
                    </>
                );
            }
            """);

        await using var host = await JsxTestHost.StartAsync(project);
        var html = await host.GetStringAsync("/client/Index");

        html.ShouldContain("<title>Products</title>");
        html.ShouldContain("""name="description" content="Everything we sell" """.TrimEnd());

        // It renders nothing where it sits: the body is the h1 and nothing else.
        html.ShouldContain("<h1>Body</h1>");
        Regex.Match(html, @"<div id=""jsxcore-root"">(?<body>.*?)</div>", RegexOptions.Singleline)
            .Groups["body"].Value.ShouldBe("<h1>Body</h1>");
    }

    [Fact]
    public async Task Head_TagsAppearInTheHeadNotTheBody()
    {
        using var project = Project("""
            "use server";
            import Head from "dotnet:rendering/head";

            export default function Index() {
                return <><Head><title>Up top</title></Head><p>body</p></>;
            }
            """);

        await using var host = await JsxTestHost.StartAsync(project);
        var html = await host.GetStringAsync("/client/Index");

        var head = html[..html.IndexOf("</head>", StringComparison.Ordinal)];
        head.ShouldContain("<title>Up top</title>");
        html[html.IndexOf("<body", StringComparison.Ordinal)..].ShouldNotContain("Up top");
    }

    [Fact]
    public async Task Head_ReadsValuesFromTheModel()
    {
        using var project = Project("""
            "use server";
            import Head from "dotnet:rendering/head";
            import type { ViewProps } from "dotnet:rendering";

            export default function Index({ model }: ViewProps<{ name: string }>) {
                return <Head><title>Hello {model.name}</title></Head>;
            }
            """);

        await using var host = await JsxTestHost.StartAsync(project);

        (await host.GetStringAsync("/client/Index")).ShouldContain("<title>Hello World</title>");
    }

    [Fact]
    public async Task Head_FromANestedComponent_StillReachesTheDocument()
    {
        // The whole point of the component form: a deep component can contribute without the view
        // having to hoist it into a head export.
        using var project = JsxProjectFixture.Create();
        project.AddView("Shared/Meta.tsx", """
            import Head from "dotnet:rendering/head";
            export function Meta() {
                return <Head><meta name="robots" content="noindex" /></Head>;
            }
            """);
        project.AddView("Home/Index.tsx", """
            "use server";
            import { Meta } from "../Shared/Meta.tsx";
            export default function Index() { return <><Meta /><p>body</p></>; }
            """);

        await using var host = await JsxTestHost.StartAsync(project);

        (await host.GetStringAsync("/client/Index"))
            .ShouldContain("""content="noindex" """.TrimEnd());
    }

    [Fact]
    public async Task Head_AndAHeadExport_BothContribute()
    {
        using var project = Project("""
            "use server";
            import Head from "dotnet:rendering/head";

            export const head = {
                title: "From the export",
                meta: [{ name: "author", content: "Someone" }]
            };

            export default function Index() {
                return <Head><title>From the component</title></Head>;
            }
            """);

        await using var host = await JsxTestHost.StartAsync(project);
        var html = await host.GetStringAsync("/client/Index");

        // The component ran later and with more information, so it wins the title.
        html.ShouldContain("<title>From the component</title>");
        html.ShouldNotContain("From the export");

        // Tags are additive: both meant what they said.
        html.ShouldContain("""content="Someone" """.TrimEnd());
    }

    [Fact]
    public async Task Head_WritesReactPropNamesAsHtmlAttributes()
    {
        // A browser ignores httpEquiv="refresh", so a page meant to work without JavaScript would
        // never redirect. The values must still be escaped.
        using var project = Project("""
            "use server";
            import Head from "dotnet:rendering/head";
            export default function Index() {
                return (
                    <Head>
                        <meta charSet="utf-8" />
                        <meta httpEquiv="refresh" content={'0;url=/next?a=1&b="<x>"'} />
                        <link rel="alternate" hrefLang="en" href="/en" crossOrigin="anonymous" />
                    </Head>
                );
            }
            """);

        await using var host = await JsxTestHost.StartAsync(project);
        var html = await host.GetStringAsync("/hybrid/Index");
        var head = html[..html.IndexOf("</head>", StringComparison.Ordinal)];

        head.ShouldContain("""<meta http-equiv="refresh" content="0;url=/next?a=1&amp;b=&quot;&lt;x&gt;&quot;" """.TrimEnd());
        head.ShouldContain("""<meta charset="utf-8" data-jsxcore-head=""");
        head.ShouldContain("""hreflang="en" """.TrimEnd());
        head.ShouldContain("""crossorigin="anonymous" """.TrimEnd());
        head.ShouldNotContain("httpEquiv", Case.Sensitive);
        head.ShouldNotContain("charSet", Case.Sensitive);
        head.ShouldNotContain("hrefLang", Case.Sensitive);
        head.ShouldNotContain("crossOrigin", Case.Sensitive);
    }

    [Fact]
    public async Task Head_DropsAttributesNoBrowserCouldAccept()
    {
        // The client's setAttribute would throw on these, so the server must not write them either.
        using var project = Project("""
            "use server";
            import Head from "dotnet:rendering/head";
            export default function Index() {
                const props = { name: "robots", content: "noindex", 'bad"name': "x", onLoad: () => 1 };
                return <Head><meta {...(props as any)} /></Head>;
            }
            """);

        await using var host = await JsxTestHost.StartAsync(project);
        var head = (await host.GetStringAsync("/hybrid/Index")).Split("</head>")[0];

        head.ShouldContain("""<meta name="robots" content="noindex" """.TrimEnd());
        head.ShouldNotContain("bad");
        head.ShouldNotContain("onLoad");
    }

    [Fact]
    public async Task Head_MarksWhatItEmitted_SoAHydratingClientCanReconcile()
    {
        using var project = Project("""
            "use server";
            import Head from "dotnet:rendering/head";
            export default function Index() {
                return <Head><meta name="description" content="x" /></Head>;
            }
            """);

        await using var host = await JsxTestHost.StartAsync(project);
        var html = await host.GetStringAsync("/hybrid/Index");

        // Without a marker the browser would append a second copy of every tag on mount.
        html.ShouldContain("data-jsxcore-head=");
    }

    [Fact]
    public async Task Head_IsResolvableByTheCompilerAndTheBrowser()
    {
        using var project = Project("""
            "use server";
            import Head from "dotnet:rendering/head";
            export default function Index() { return <Head><title>t</title></Head>; }
            """);

        var build = await project.CompileAsync();
        build.Result.Diagnostics.ShouldBeEmpty();

        await using var host = await JsxTestHost.StartAsync(project);
        var html = await host.GetStringAsync("/hybrid/Index");

        // The browser resolves the sub-path through the import map, not by guessing.
        var map = Regex.Match(html, @"<script type=""importmap""[^>]*>(?<json>.*?)</script>",
            RegexOptions.Singleline).Groups["json"].Value;

        map.ShouldContain("dotnet:rendering/head");
    }

    [Fact]
    public async Task Head_OnAClientRenderedView_IsLeftToTheBrowser()
    {
        // The component is not run on the server in this mode, so its tags cannot be in the first
        // response. The head export is the tool for that, and this records the difference.
        using var project = Project("""
            "use client";
            import Head from "dotnet:rendering/head";
            export default function Index() { return <Head><title>Client only</title></Head>; }
            """);

        await using var host = await JsxTestHost.StartAsync(project);
        var html = await host.GetStringAsync("/client/Index");

        html.ShouldNotContain("<title>Client only</title>");
        html.ShouldContain("mountView");
    }
}
