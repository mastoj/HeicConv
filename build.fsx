// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------
#r "paket: groupref build //"
#load ".fake/build.fsx/intellisense.fsx"

open Fake.Core
open Fake.DotNet
open Fake.Tools
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators
open Fake.Api

// --------------------------------------------------------------------------------------
// Information about the project to be used at NuGet and in AssemblyInfo files
// --------------------------------------------------------------------------------------

let project = "HeiConv"

let summary = "HEIC to JPG file converter. The goal for this application is simplicity over a lot of functionality."

let gitOwner = "mastoj"
let gitName = "heiconv"
let gitHome = "https://github.com/" + gitOwner

// --------------------------------------------------------------------------------------
// Build variables
// --------------------------------------------------------------------------------------

let buildDir  = "./build/"

System.Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
let release = ReleaseNotes.parse (System.IO.File.ReadAllLines "RELEASE_NOTES.md")

let runtimes = [ "osx-x64"; "win-x64" ]

// --------------------------------------------------------------------------------------
// Helpers
// --------------------------------------------------------------------------------------
let isNullOrWhiteSpace = System.String.IsNullOrWhiteSpace
let exec cmd args dir =
    if Process.execSimple( fun info ->

        { info with
            FileName = cmd
            WorkingDirectory =
                if (isNullOrWhiteSpace dir) then info.WorkingDirectory
                else dir
            Arguments = args
            }
    ) System.TimeSpan.MaxValue <> 0 then
        failwithf "Error while running '%s' with args: %s" cmd args
let getBuildParam = Environment.environVar

let DoNothing = ignore
// --------------------------------------------------------------------------------------
// Build Targets
// --------------------------------------------------------------------------------------

Target.create "Clean" (fun _ ->
    Shell.cleanDirs [buildDir]
)

Target.create "AssemblyInfo" (fun _ ->
    let getAssemblyInfoAttributes projectName =
        [ AssemblyInfo.Title projectName
          AssemblyInfo.Product project
          AssemblyInfo.Description summary
          AssemblyInfo.Version release.AssemblyVersion
          AssemblyInfo.FileVersion release.AssemblyVersion ]

    let getProjectDetails projectPath =
        let projectName = System.IO.Path.GetFileNameWithoutExtension(projectPath)
        ( projectPath,
          projectName,
          System.IO.Path.GetDirectoryName(projectPath),
          (getAssemblyInfoAttributes projectName)
        )

    !! "src/**/*.??proj"
    |> Seq.map getProjectDetails
    |> Seq.iter (fun (projFileName, _, folderName, attributes) ->
        match projFileName with
        | proj when proj.EndsWith("fsproj") -> AssemblyInfoFile.createFSharp (folderName </> "AssemblyInfo.fs") attributes
        | proj when proj.EndsWith("csproj") -> AssemblyInfoFile.createCSharp ((folderName </> "Properties") </> "AssemblyInfo.cs") attributes
        | proj when proj.EndsWith("vbproj") -> AssemblyInfoFile.createVisualBasic ((folderName </> "My Project") </> "AssemblyInfo.vb") attributes
        | _ -> ()
        )
)

Target.create "Restore" (fun _ ->
    DotNet.restore id ""
)

Target.create "Build" (fun _ ->
    runtimes
    |> List.iter (fun runtime ->
            let getBuildOptions (options: DotNet.BuildOptions) =
                { 
                    options with
                        Runtime = Some runtime
                }
            DotNet.build getBuildOptions ""
    )        
)

// --------------------------------------------------------------------------------------
// Release Targets
// --------------------------------------------------------------------------------------

Target.create "Publish" (fun _ ->
    [ "osx-x64"; "win-x64"]
    |> List.iter (
        fun runtime ->
            let getOptions (options: DotNet.PublishOptions) = 
                { 
                    options with
                        Runtime = Some runtime
                        OutputPath = Some (Path.combine buildDir runtime)
                }
            DotNet.publish getOptions ""
    )
)

Target.create "CopyBinaries" (fun _ ->
    let outFolder = sprintf "%sout/" buildDir
    System.IO.Directory.CreateDirectory(outFolder) |> ignore
    let fileMappings = [
        "osx-x64/heiconv", "heiconv_osx-x64"
        "win-x64/heiconv.exe", "heiconv_win-x64.exe"
    ]
    fileMappings
    |> List.iter (fun (source, target) ->
        let sourcePath = sprintf "%s%s" buildDir source
        let targetPath = sprintf "%s%s" outFolder target
        System.IO.File.Copy(sourcePath, targetPath, true)
    )
)

Target.create "ReleaseGitHub" (fun _ ->
    let remote =
        Git.CommandHelper.getGitResult "" "remote -v"
        |> Seq.filter (fun (s: string) -> s.EndsWith("(push)"))
        |> Seq.tryFind (fun (s: string) -> s.Contains(gitOwner + "/" + gitName))
        |> function None -> gitHome + "/" + gitName | Some (s: string) -> s.Split().[0]

    // Git.Staging.stageAll ""
    // Git.Commit.exec "" (sprintf "Bump version to %s" release.NugetVersion)
    // Git.Branches.pushBranch "" remote (Git.Information.getBranchName "")


    Git.Branches.tag "" release.NugetVersion
    Git.Branches.pushTag "" remote release.NugetVersion

    let client =
        let token =
            match getBuildParam "github_token" with
            | s when not (isNullOrWhiteSpace s) -> s
            | _ -> UserInput.getUserInput "Token: "

        // Git.createClient user pw
        GitHub.createClientWithToken token
    let files = !! (buildDir </> "out/*")

    // release on github
    let cl =
        client
        |> GitHub.draftNewRelease gitOwner gitName release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes
    (cl,files)
    ||> Seq.fold (fun acc e -> acc |> GitHub.uploadFile e)
    |> GitHub.publishDraft//releaseDraft
    |> Async.RunSynchronously
)

// Target.create "Push" (fun _ ->
//     let key =
//         match getBuildParam "nuget-key" with
//         | s when not (isNullOrWhiteSpace s) -> s
//         | _ -> UserInput.getUserPassword "NuGet Key: "
//     Paket.push (fun p -> { p with WorkingDir = buildDir; ApiKey = key }))

// --------------------------------------------------------------------------------------
// Build order
// --------------------------------------------------------------------------------------
Target.create "Default" DoNothing
Target.create "Release" DoNothing

"Clean"
//   ==> "InstallDotNetCLI"
  ==> "AssemblyInfo"
  ==> "Restore"
  ==> "Build"
  ==> "Default"

"Default"
  ==> "Publish"
  ==> "CopyBinaries"
  ==> "ReleaseGitHub"
//  ==> "Push"
  ==> "Release"

Target.runOrDefault "Default"