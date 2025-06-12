using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.ObjectStore.Server;

public class DataStore
{
    private readonly DirectoryInfo rootDirectory;

    public DataStore(DirectoryInfo _rootDirectory)
    {
        rootDirectory = _rootDirectory;

        if (!rootDirectory.Exists)
        {
            rootDirectory.Create();
        }
    }

    public string store(byte[] data)
    {
        string id = U.RandomUuid().ToString();

        DirectoryInfo dir = new DirectoryInfo(Path.Combine(rootDirectory.FullName, id[..2]));
        if (!dir.Exists)
        {
            dir.Create();
        }

        FileInfo file = new FileInfo(Path.Combine(dir.FullName, id));

        try
        {
            using (FileStream fileOutputStream = file.OpenWrite())
            {
                fileOutputStream.Write(data);
            }
        }
        catch (IOException ex)
        {
            file.Delete();
            throw new DataStoreException(ex);
        }

        return id;
    }

    public byte[]? load(string id)
    {
        FileInfo file = new FileInfo(Path.Combine(rootDirectory.FullName, id[..2], id));
        if (!file.Exists)
        {
            return null;
        }

        MemoryStream byteArrayOutputStream;
        try
        {
            byteArrayOutputStream = new MemoryStream((int)file.Length);
        }
        catch (IOException ex)
        {
            throw new DataStoreException(ex);
        }

        try
        {
            using (FileStream fileInputStream = file.OpenRead())
                fileInputStream.CopyTo(byteArrayOutputStream);
        }

        catch (IOException ex)
        {
            throw new DataStoreException(ex);
        }

        byte[] data = byteArrayOutputStream.ToArray();

        return data;
    }

    public void delete(string id)
    {
        FileInfo file = new FileInfo(Path.Combine(rootDirectory.FullName, id.Substring(0, 2), id));
        file.Delete();
    }

    public class DataStoreException : Exception
    {
        public DataStoreException(string? message)
            : base(message)
        {
        }

        public DataStoreException(Exception? cause)
            : base(null, cause)
        {
        }
    }
}
