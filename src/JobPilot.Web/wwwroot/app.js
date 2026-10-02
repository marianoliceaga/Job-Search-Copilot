// Downloads a file streamed from the server (Blazor DotNetStreamReference).
window.jobPilot = {
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
};
