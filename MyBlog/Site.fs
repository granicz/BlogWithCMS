module Site

open WebSharper
open WebSharper.Sitelets
open WebSharper.UI
open WebSharper.UI.Html
open WebSharper.UI.Server
open WebSharper.UI.Templating
open WebSharper.CMS
open WebSharper.UI.Client

type EndPoint =
    | [<EndPoint "/">] Home
    | [<EndPoint "/post">] Post of slug:string

type PostTemplate = Template<"post.html", clientLoad=ClientLoad.FromDocument, serverLoad=ServerLoad.WhenChanged>

module UI =
    let LatestArticleCard (ctx: Context<_>, inEdit) id =
        let title = MyFileCMS.Read(ctx.RootFolder, sprintf "title_%d" id) |> Option.defaultValue ""
        PostTemplate.LatestCard()
            .Title(title)
            .Url(if inEdit then sprintf "/edit/post/%d" id else sprintf "/post/%d" id)
            .ThumbnailUrl(sprintf "/assets/img/demopic/%d.jpg" (id % 10))
            .Doc()

[<Website>]
let Main =
    fun (ctx: Context<_>) (inEdit: bool) -> function
        | Home ->
            MainTemplate()
                .Content(
                    [
                        h1 [] [text "Hello, this is my blog!"]
                        p [] [a [attr.href <| ctx.Link (Post "3")] [text "Here is a random blog article, prefix it with `/edit` to go into edit mode"]]
                    ]
                )
                .Doc()
        | Post slug ->
            PostTemplate()
                .Title(
                    Doc.ManagedContent (ctx, inEdit) ("title_" + slug, Some <| sprintf "This is blog post %s" slug)
                )
                .Content(
                    Doc.ManagedContentWithInPlaceEditor (ctx, inEdit) ("post_" + slug, None)
                )
                .LatestPosts(
                    [
                        UI.LatestArticleCard (ctx, inEdit) 1
                        UI.LatestArticleCard (ctx, inEdit) 2
                        UI.LatestArticleCard (ctx, inEdit) 3
                    ]
                )
                .Doc()
    |> CMS.AddCMS
