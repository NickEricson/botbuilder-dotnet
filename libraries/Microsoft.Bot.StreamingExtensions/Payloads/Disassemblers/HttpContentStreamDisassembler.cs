﻿using System.Threading.Tasks;
using Microsoft.Bot.StreamingExtensions.PayloadTransport;

namespace Microsoft.Bot.StreamingExtensions.Payloads
{
    public class HttpContentStreamDisassembler : PayloadDisassembler
    {
        public HttpContentStreamDisassembler(IPayloadSender sender, HttpContentStream contentStream)
            : base(sender, contentStream.Id)
        {
            ContentStream = contentStream;
        }

        public HttpContentStream ContentStream { get; private set; }

        public override char Type => PayloadTypes.Stream;

        public override async Task<StreamWrapper> GetStream()
        {
            var stream = await ContentStream.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var description = GetStreamDescription(ContentStream);

            return new StreamWrapper()
            {
                Stream = stream,
                StreamLength = description.Length,
            };
        }
    }
}
