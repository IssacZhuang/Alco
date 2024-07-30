namespace SlangSharp;

public unsafe struct ISlangBlob
{
    public VTable* Vtbl;

    public void* GetBufferPointer()
    {
        fixed (ISlangBlob* pThis = &this)
        {
            return Vtbl->GetBufferPointer(pThis);
        }
    }

    public nuint GetBufferSize()
    {
        fixed (ISlangBlob* pThis = &this)
        {
            return Vtbl->GetBufferSize(pThis);
        }
    }

    public struct VTable
    {
        //ISlangUnknown
        public delegate* unmanaged[Stdcall]<ISlangBlob*, SlangUUID*, void**, SlangResult> QueryInterface;
        public delegate* unmanaged[Stdcall]<ISlangBlob*, uint> AddRef;
        public delegate* unmanaged[Stdcall]<ISlangBlob*, uint> Release;
        //ISlangBlob
        public delegate* unmanaged[Stdcall]<ISlangBlob*, void*> GetBufferPointer;
        public delegate* unmanaged[Stdcall]<ISlangBlob*, nuint> GetBufferSize;
    }
}

public unsafe struct ISlangFileSystem
{
    public VTable* Vtbl;

    public SlangResult LoadFile(byte* path, out ISlangBlob* outBlob)
    {
        fixed (ISlangFileSystem* pThis = &this)
        {

            ISlangBlob* blob;
            SlangResult result = Vtbl->LoadFile(pThis, path, &blob);
            outBlob = blob;
            return result;
        }
    }


    public struct VTable
    {
        //ISlangUnknown
        public delegate* unmanaged[Stdcall]<ISlangFileSystem*, SlangUUID*, void**, SlangResult> QueryInterface;
        public delegate* unmanaged[Stdcall]<ISlangFileSystem*, uint> AddRef;
        public delegate* unmanaged[Stdcall]<ISlangFileSystem*, uint> Release;
        //ISlangFileSystem
        public delegate* unmanaged[Stdcall]<ISlangFileSystem*, byte*, ISlangBlob**, SlangResult> LoadFile;
    }
}

// struct ISlangUnknown
// {
//     SLANG_COM_INTERFACE(0x00000000, 0x0000, 0x0000, { 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46 })

//         virtual SLANG_NO_THROW SlangResult SLANG_MCALL queryInterface(SlangUUID const& uuid, void** outObject) = 0;
//     virtual SLANG_NO_THROW uint32_t SLANG_MCALL addRef() = 0;
//         virtual SLANG_NO_THROW uint32_t SLANG_MCALL release() = 0;

//         /*
//         Inline methods are provided to allow the above operations to be called
//         using their traditional COM names/signatures:
//         */
//         SlangResult QueryInterface(struct _GUID const& uuid, void** outObject) { return queryInterface(*(SlangUUID const*)&uuid, outObject); }
// uint32_t AddRef() { return addRef(); }
// uint32_t Release() { return release(); }
//     };

// struct ISlangFileSystem : public ISlangCastable
//     {
//         SLANG_COM_INTERFACE(0x003A09FC, 0x3A4D, 0x4BA0, { 0xAD, 0x60, 0x1F, 0xD8, 0x63, 0xA9, 0x15, 0xAB })

//         /** Load a file from `path` and return a blob of its contents
//         @param path The path to load from, as a null-terminated UTF-8 string.
//         @param outBlob A destination pointer to receive the blob of the file contents.
//         @returns A `SlangResult` to indicate success or failure in loading the file.

//         NOTE! This is a *binary* load - the blob should contain the exact same bytes
//         as are found in the backing file. 

//         If load is successful, the implementation should create a blob to hold
//         the file's content, store it to `outBlob`, and return 0.
//         If the load fails, the implementation should return a failure status
//         (any negative value will do).
//         */
//         virtual SLANG_NO_THROW SlangResult SLANG_MCALL loadFile(
//             char const*     path,
//             ISlangBlob** outBlob) = 0;
//     };


// struct ISlangBlob : public ISlangUnknown
//     {
//         SLANG_COM_INTERFACE(0x8BA5FB08, 0x5195, 0x40e2, { 0xAC, 0x58, 0x0D, 0x98, 0x9C, 0x3A, 0x01, 0x02 })

//         virtual SLANG_NO_THROW void const* SLANG_MCALL getBufferPointer() = 0;
//         virtual SLANG_NO_THROW size_t SLANG_MCALL getBufferSize() = 0;
//     };