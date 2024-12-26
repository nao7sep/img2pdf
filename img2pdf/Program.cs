using iText.IO.Image;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace img2pdf
{
    class Program
    {
        static void Main (string [] args)
        {
            try
            {
                // In compliance with the AGPL license, the following message must be displayed.
                Console.WriteLine ("Project Page: https://github.com/nao7sep/img2pdf");

                if (args.Length == 0)
                {
                    Console.WriteLine ("Usage: img2pdf.exe <image1> <image2> ...");
                    return;
                }

                string [] xSupportedExtensions = [ ".bmp", ".gif", ".jpg", ".jpeg", ".png", ".tif", ".tiff" ];

                foreach (string xSourceDirectoryPath in args)
                {
                    if (Directory.Exists (xSourceDirectoryPath) == false)
                        throw new DirectoryNotFoundException (xSourceDirectoryPath);

                    if (Directory.GetFiles (xSourceDirectoryPath, "*.*", SearchOption.TopDirectoryOnly).
                            Count (x => xSupportedExtensions.Contains (System.IO.Path.GetExtension (x).ToLower ())) < 2)
                        throw new ArgumentException ("Contains less than two images: " + xSourceDirectoryPath);
                }

                // _imageMerger currently doesnt copy resolution information from the original images to the merged ones
                // because, if it does, the simplicity of "not inheriting any metadata" will be lost and we'll have to continually wonder, "What else?"
                // Then, whenever we find something new that seems relevant and decide to copy it, we'll have to decide whether to regenerate all the existing merged images or not.
                // That would be highly unproductive.

                // When this program combines images into a PDF file, merged ones by _imageMerger would return 72 DPI as their resolution.
                // This value is often returned from photo images taken by cameras as well, which makes it a bad choice to auto-change 72 DPI to the default value or a given one.

                // When we wish to auto-combine images into a PDF file, it is reasonable to assume that they are from the same source and have the same resolution.
                // Otherwise, we'd use a proper app and manually structure the document before saving it as a PDF file.
                // The best practice should be asking the user to provide the resolution of the original images.

                int? xOriginalResolution;

                while (true)
                {
                    Console.Write ("Resolution of original images (DPI): ");
                    string? xOriginalResolutionString = Console.ReadLine ();

                    if (int.TryParse (xOriginalResolutionString, out int xValue) && xValue > 0)
                    {
                        xOriginalResolution = xValue;
                        break;
                    }
                }

                int? xResizeFactor;

                while (true)
                {
                    Console.Write ("Divide image dimensions by: ");
                    string? xResizeFactorString = Console.ReadLine ();

                    if (int.TryParse (xResizeFactorString, out int xValue) && xValue > 0)
                    {
                        xResizeFactor = xValue;
                        break;
                    }
                }

                double xNewResolution = (double) xOriginalResolution / xResizeFactor.Value;

                // Very limited information is found about the compression settings of iText7.
                // These shouldnt be negatively affecting anything.
                // SetFullCompressionMode => Defines if full compression mode is enabled. If enabled, not only the content of the pdf document will be compressed, but also the pdf document inner structure.
                // SetCompressionLevel => Defines the level of compression for the document.
                var xWriterProperties = new WriterProperties ().SetFullCompressionMode (true).SetCompressionLevel (CompressionConstants.BEST_COMPRESSION);

                foreach (string xSourceDirectoryPath in args)
                {
                    try
                    {
                        string xPdfFilePath = System.IO.Path.ChangeExtension (xSourceDirectoryPath, ".pdf");

                        using var xPdfWriter = new PdfWriter (xPdfFilePath, xWriterProperties); // Overwrites the file if it exists.
                        using var xPdfDocument = new PdfDocument (xPdfWriter);
                        using var xDocument = new Document (xPdfDocument);

                        // In Windows Explorer, files are sorted in a natural order, meaning the extensions are included, numbers are parsed and the punctuation marks are treated uniquely.
                        // In the ASCII table, characters such as ' ', '(' and '-' come before '.'.
                        // If extensions are included, "file.jpg" may come after "file (1).jpg".
                        // But if we exclude them, files may be sorted differently than they are in other apps, where we work with files.
                        // The best practice should be to make sure that '_' is used to attach numbers to file names so that '.' will come before it.

                        string [] xImageFilePaths = Directory.GetFiles (xSourceDirectoryPath, "*.*", SearchOption.TopDirectoryOnly).
                            Where (x => xSupportedExtensions.Contains (System.IO.Path.GetExtension (x).ToLower ())).Order (StringComparer.OrdinalIgnoreCase).ToArray ();

                        for (int temp = 0; temp < xImageFilePaths.Length; temp ++)
                        {
                            Console.Write ($"\rAdding image {temp + 1} of {xImageFilePaths.Length}...");

                            using var xOriginalImage = SixLabors.ImageSharp.Image.Load <Rgba32> (xImageFilePaths [temp]);

                            int xOriginalWidth = xOriginalImage.Width,
                                xOriginalHeight = xOriginalImage.Height;

                            int xNewWidth = (int) Math.Round ((double) xOriginalWidth / xResizeFactor.Value),
                                xNewHeight = (int) Math.Round ((double) xOriginalHeight / xResizeFactor.Value);

                            // 72 DPI is not the only choice for the resolution of a PDF file.
                            // But historically, it is considered to be: Suitable for PDFs intended primarily for on-screen viewing, web distribution, or general office use.

                            float xNewWidthInPoints = (float) (xNewWidth * 72f / xNewResolution),
                                  xNewHeightInPoints = (float) (xNewHeight * 72f / xNewResolution);

                            // The default resampling algorithm is Bicubic according to the source code.
                            // https://docs.sixlabors.com/api/ImageSharp/SixLabors.ImageSharp.Processing.KnownResamplers.html
                            xOriginalImage.Mutate (x => x.Resize (xNewWidth, xNewHeight));

                            // We make a new image and copy the resized one to it.
                            // ImageSharp can strip metadata from images, but let's be extra cautious.
                            // We dont make the same PDF files repeatedly, but the files are sent to many people.

                            using var xNewImage = new SixLabors.ImageSharp.Image <Rgba32> (xNewWidth, xNewHeight);
                            xNewImage.Mutate (x => x.DrawImage (xOriginalImage, opacity: 1));

                            using var xMemoryStream = new MemoryStream ();
                            var xEncoder = new JpegEncoder { Quality = 75, SkipMetadata = true };
                            xNewImage.Save (xMemoryStream, xEncoder);
                            // xMemoryStream.Position = 0; >= Doesnt seem necessary.

                            var xImageData = ImageDataFactory.Create (xMemoryStream.ToArray ());
                            var xImage = new Image (xImageData);
                            xImage.SetFixedPosition (left: 0, bottom: 0);
                            xImage.ScaleToFit (xNewWidthInPoints, xNewHeightInPoints);

                            // This must be called before adding the AreaBreak.
                            // Otherwise, pages' sizes and their corresponding images' sizes may differ.
                            // Like, when I combined a vertical image, a horizontal one and a vertical one, the 3rd image had a horizontal canvas, the rest of which was filled with white.
                            xPdfDocument.SetDefaultPageSize (new PageSize (xNewWidthInPoints, xNewHeightInPoints));

                            if (temp > 0)
                                // https://stackoverflow.com/questions/40859431/adding-a-new-page-in-pdf-using-itext-7
                                xDocument.Add (new AreaBreak (AreaBreakType.NEXT_PAGE));

                            xDocument.Add (xImage);
                        }

                        Console.WriteLine ("\rPDF file created: " + xPdfFilePath);
                    }

                    catch (Exception xException)
                    {
                        // Resizing images and generating PDF files are time-consuming tasks.
                        // If something wrong happens, the program should continue with the next directory.

                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine (xException.ToString ());
                        Console.ResetColor ();
                    }
                }
            }

            catch (Exception xException)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine (xException.ToString ());
                Console.ResetColor ();
            }

            finally
            {
                Console.Write ("Press any key to exit: ");
                Console.ReadKey (true);
                Console.WriteLine ();
            }
        }
    }
}
