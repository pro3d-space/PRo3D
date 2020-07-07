module Client

open System.Net.Sockets

type Client(address, port) =
    
    member x.client = new TcpClient(address, port);
    member x.Close() = x.client.Close()

    member x.SendMessage (message : string) =
        let data = System.Text.Encoding.ASCII.GetBytes(message)
        let stream = x.client.GetStream()
        // printfn "Sending message:\n%A" message
        stream.Write(data, 0, data.Length)

        stream.Close()

    member x.SendMessageWaitForResponse (message : string) =
        let data = System.Text.Encoding.ASCII.GetBytes(message)
        let stream = x.client.GetStream()
        printfn "Sending message:\n%A" message
        stream.Write(data, 0, data.Length)

        let bytes = stream.Read(data, 0, data.Length);
        let responseData = System.Text.Encoding.ASCII.GetString(data, 0, bytes);
        printfn "Receiving response: %A" responseData

        stream.Close()
