window.jobPilot = {
    // Downloads a file streamed from the server (Blazor DotNetStreamReference).
    downloadFile: async (fileName, contentType, streamReference) => {
        const buffer = await streamReference.arrayBuffer();
        const url = URL.createObjectURL(new Blob([buffer], { type: contentType }));
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = fileName ?? "";
        anchor.click();
        anchor.remove();
        URL.revokeObjectURL(url);
    },
    // Copies a draft to the clipboard. The app never sends messages: the owner pastes and sends them.
    copyText: async (text) => {
        await navigator.clipboard.writeText(text);
    },
};
