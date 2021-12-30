namespace WebSharper.CMS

open WebSharper
open WebSharper.Sitelets
open WebSharper.UI
open WebSharper.UI.Server
open WebSharper.UI.Templating

type MainTemplate = Template<"main.html", serverLoad=ServerLoad.WhenChanged>

module CMS =
    type CMS<'T> =
        | [<EndPoint "/edit">] Edit of 'T
        | [<EndPoint "/">] View of 'T

    let AddCMS (render: Context<'T> -> bool -> 'T -> Doc) : Sitelet<CMS<'T>> =
        Sitelet.Infer (fun (ctx: Context<CMS<'T>>) -> function
            | Edit (page: 'T) ->
                Content.Page (render (Context.Map Edit ctx) true page)
            | View (page: 'T) ->
                Content.Page (render (Context.Map View ctx) false page)
        )

module MyFileCMS =
    open System.IO

    let Write (sourceFolder, key, newValue) =
        let fname = sprintf "%s/store/%s.html" sourceFolder key
        // Check whether target directory exists, if not, create it
        let dir = Path.GetDirectoryName fname
        if not (Directory.Exists dir) then
            Directory.CreateDirectory dir |> ignore
        File.WriteAllText(fname, newValue)

    let Read (sourceFolder, key) =
        let fname = sprintf "%s/store/%s.html" sourceFolder key
        if File.Exists fname then
            Some <| File.ReadAllText fname
        else
            None

module Server =
    [<Rpc>]
    let WriteContent (key: string, newValue) =
        let ctx = Web.Remoting.GetContext()
        async {
            return MyFileCMS.Write(ctx.RootFolder, key, newValue)
        }

    [<Rpc>]
    let ReadContent key = 
        let ctx = Web.Remoting.GetContext()
        async {
            return MyFileCMS.Read(ctx.RootFolder, key)
        }

open WebSharper.JavaScript
open WebSharper.UI.Client
open WebSharper.UI.Html
open WebSharper.UI.Notation

[<JavaScript>]
module Client =
    let SleepThen (ms, f: unit -> unit) =
        JS.Window.SetTimeout(new System.Action(f), ms) |> ignore

type ManagedContentResource() =
    inherit Resources.BaseResource("WebSharper.CMS.css.ManagedContent.css")

[<AutoOpen>]
module Extensions =
    type Doc with
        [<Require(typeof<ManagedContentResource>)>]
        static member ManagedContent (ctx: Context<_>, inEdit) (key, defValue) =
            let v = MyFileCMS.Read(ctx.RootFolder, key)
            let content = Option.defaultValue (Option.defaultValue "" defValue) v
            if inEdit then
                Doc.Concat [
                    MainTemplate.Editor()
                        .OnAfterRender(fun (e: Runtime.Server.TemplateEvent<MainTemplate.Editor.Vars, _>) ->
                            async {
                                // Access the loading indicator - hack
                                let loader = e.Target.QuerySelector(".loader-container")
                                loader.ClassList.Add("show") |> ignore
                                // Retrieve saved value for our given key
                                let! res = Server.ReadContent key
                                do if res.IsSome then e.Vars.TextContent := res.Value
                                // Remove the loading indicator after a bit of waiting
                                Client.SleepThen(250, (fun () ->
                                    loader.ClassList.Remove("show") |> ignore)
                                )
                            }
                            |> Async.Start
                        )
                        .Save(fun (e: Runtime.Server.TemplateEvent<MainTemplate.Editor.Vars, _>) ->
                            // Disable save button
                            e.Target.SetAttribute("disabled", "true")
                            e.Target.TextContent <- "Saving..."
                            // Save what's in the editor
                            async {
                                do! Server.WriteContent(key, e.Vars.TextContent.Value)
                                // Re-enable save button
                                JS.Window.SetTimeout(
                                    fun () ->
                                        e.Target.RemoveAttribute "disabled"
                                        e.Target.TextContent <- "Save"
                                    , 250
                                ) |> ignore
                            } |> Async.Start
                        )
                        .TextContent(content)
                        .Doc()
                    // Add the control's CSS file as a resource.
                    Doc.WebControl <| Web.Require(typeof<ManagedContentResource>)
                ]
            else
                if v.IsNone && defValue.IsNone then
                    Doc.Empty
                else
                    text content

    type Doc with
        /// A UI control that can edit its content on demand.
        /// It must be attached to a container node (p, div, etc.)
        static member ManagedContentWithInPlaceEditor (ctx: Context<_>, inEdit) (key, defValue) =
            let v = MyFileCMS.Read(ctx.RootFolder, key)
            let content = Option.defaultValue (Option.defaultValue "" defValue) v
            if inEdit then
                MainTemplate.InPlaceEditor()
                    .OnAfterRender(fun (e: Runtime.Server.TemplateEvent<MainTemplate.InPlaceEditor.Vars, _>) ->
                        // Hijack ENTERs so we don't get inner container nodes.
                        // https://stackoverflow.com/questions/18552336/prevent-contenteditable-adding-div-on-enter-chrome
                        e.Target.ParentElement.AddEventListener("keydown",
                            new System.Action<Dom.Event>(fun e ->
                                let e = e :?> Dom.KeyboardEvent
                                if e.KeyCode = 13 then
                                    JS.Document.ExecCommand("insertHTML", false, "<br/>") |> ignore
                                    e.PreventDefault()
                            ))
                        // Auto-save when container/editor node loses focus
                        e.Target.ParentElement.AddEventListener("blur",
                            new System.Action<Dom.Event>(fun e ->
                                let target = As<Dom.Element> JS.this
                                let parent = target.Closest("[contenteditable=true]")
                                let shadow = JS.Document.CreateElement("div")
                                parent.ChildNodes.ForEach((fun (node, _, _, _) ->
                                    shadow.AppendChild(node.CloneNode(true)) |> ignore
                                ), null)
                                let content = shadow.InnerHTML
                                Console.Log <| sprintf "Final=[%s]" content
                                async {
                                    do! Server.WriteContent(key, content)
                                } |> Async.Start
                            ))
                        // Make the parent control editable
                        e.Target.ParentElement.SetAttribute("contenteditable", "true")
                        // Final step: hack to remove outer div coming from the template
                        e.Target.ParentElement.InnerHTML <- e.Target?innerHTML
                    )
                    .TextContent(Doc.Verbatim content)
                    .Doc()
            else
                if v.IsNone && defValue.IsNone then
                    Doc.Empty
                else
                    Doc.Verbatim content

[<assembly: WebResource("WebSharper.CMS.css.ManagedContent.css", "text/css")>]
do ()
