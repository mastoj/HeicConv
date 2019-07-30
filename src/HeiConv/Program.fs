// Learn more about F# at http://fsharp.org

open System
open System.IO
open ImageMagick
open Argu

type FileTypes =
    | Heic
    | JPG

let fileTypeToExt = function
    | Heic -> "heic"
    | JPG -> "jpg"

let getFullPath path =
    Path.GetFullPath path

let getFileName (path: string) =
    Path.GetFileName(path)

let getFileNameWithoutExtension (path: string) =
    Path.GetFileNameWithoutExtension(path)

let getDirectory (path: string) =
    Path.GetDirectoryName(path)

let createFileName fileType fileName =
    sprintf "%s.%s" fileName (fileType |> fileTypeToExt)

let combine directory fileName =
    Path.Combine(directory, fileName)

let makeSureDirExists dir =
    Directory.CreateDirectory(dir)

let getSourceAndTarget targetFileType path targetDir =
    let fullPath = path |> getFullPath
    let directory = 
        match targetDir with
        | Some td -> td |> getFullPath
        | None -> fullPath |> getDirectory
    let directoryInfo = directory |> makeSureDirExists
    let fileNameWithoutExtension = fullPath |> getFileNameWithoutExtension
    let targetPath =
        fileNameWithoutExtension
        |> createFileName targetFileType
        |> combine (directoryInfo.FullName)
    (fullPath, targetPath)

let convert targetFileType path targetDir =
    let (fullPath, targetPath) = getSourceAndTarget targetFileType path targetDir
    printfn "Converting %s -> %s" fullPath targetPath
    use img = new MagickImage(fullPath)
    img.Write(targetPath)

type Argument =
    | [<AltCommandLine("-f")>]File of file:string
    | [<AltCommandLine("-d")>]Dir of dir:string
    | [<AltCommandLine("-o")>]Outdir of outdir:string
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | File _ -> "File to convert"
            | Dir _ -> "Directory to convert"
            | Outdir _ -> "Directory to store the output (will store side by side if not provided)"

let parseArguments args =
    let parser = ArgumentParser.Create<Argument>(programName = "heiconv.exe")
    let usage = parser.PrintUsage()
    let result = parser.Parse args
    result.GetAllResults()

type JobType =
    | SingleFile of path: string
    | Directory of path: string

type Job =
    {
        JobType: JobType
        TargetDir: string option
    }

let validateArguments (arguments: Argument list) =
    let folder (jobType, targetDir) state =
        match state with
        | File s -> (Some (SingleFile s), targetDir)
        | Dir s -> (Some (Directory s), targetDir)
        | Outdir s ->
            (jobType, Some s)

    let (jobType, targetDir) =
        arguments
        |> List.fold folder (None, None)
    match (jobType, targetDir) with
    | Some jt, x -> { JobType = jt; TargetDir = x }
    | _, _ -> raise (exn "Invalid arguments")

let getAllHeicFilesFromDirectory path =
    Directory.GetFiles(path |> getFullPath, "*." + (Heic |> fileTypeToExt)) |> List.ofArray

let executeJob job =
    let simpleConvert file = convert JPG file job.TargetDir
    match job.JobType with
    | SingleFile path -> simpleConvert path
    | Directory path ->
        getAllHeicFilesFromDirectory path
        |> List.iter simpleConvert

[<EntryPoint>]
let main argv =
    argv
    |> parseArguments
    |> validateArguments
    |> executeJob
    0 // return an integer exit code
