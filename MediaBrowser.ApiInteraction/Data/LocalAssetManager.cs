﻿using MediaBrowser.Model.ApiClient;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Sync;
using MediaBrowser.Model.Users;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MediaBrowser.ApiInteraction.Data
{
    public class LocalAssetManager
    {
        private readonly IUserActionRepository _userActionRepository;
        private readonly IItemRepository _itemRepository;
        private readonly IFileRepository _fileRepository;

        public LocalAssetManager(IUserActionRepository userActionRepository, IItemRepository itemRepository, IFileRepository fileRepository)
        {
            _userActionRepository = userActionRepository;
            _itemRepository = itemRepository;
            _fileRepository = fileRepository;
        }

        /// <summary>
        /// Records the user action.
        /// </summary>
        /// <param name="action">The action.</param>
        /// <returns>Task.</returns>
        public Task RecordUserAction(UserAction action)
        {
            action.Id = Guid.NewGuid().ToString("N");

            return _userActionRepository.Create(action);
        }

        /// <summary>
        /// Deletes the specified action.
        /// </summary>
        /// <param name="action">The action.</param>
        /// <returns>Task.</returns>
        public Task Delete(UserAction action)
        {
            return _userActionRepository.Delete(action);
        }

        /// <summary>
        /// Gets all user actions by serverId
        /// </summary>
        /// <param name="serverId"></param>
        /// <returns></returns>
        public Task<IEnumerable<UserAction>> GetUserActions(string serverId)
        {
            return _userActionRepository.Get(serverId);
        }

        /// <summary>
        /// Adds the or update.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns>Task.</returns>
        public Task AddOrUpdate(BaseItemDto item)
        {
            return _itemRepository.AddOrUpdate(item);
        }

        /// <summary>
        /// Gets the files.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="server">The server.</param>
        /// <returns>Task&lt;List&lt;ItemFileInfo&gt;&gt;.</returns>
        public async Task<List<ItemFileInfo>> GetFiles(BaseItemDto item, ServerInfo server)
        {
            var path = GetDirectoryPath(item, server);

            var list = await _fileRepository.GetFileSystemEntries(path).ConfigureAwait(false);

            var itemFiles = new List<ItemFileInfo>();

            foreach (var file in list)
            {
                var itemFile = new ItemFileInfo
                {
                    Path = file.Path, 
                    Name = file.Name, 
                    ItemId = item.Id
                };

                if (IsImageFile(file.Name))
                {
                    itemFile.Type = ItemFileType.Image;
                    itemFile.ImageType = GetImageType(file.Name);
                }
                else if (IsSubtitleFile(file.Name))
                {
                    itemFile.Type = ItemFileType.Subtitles;
                }
                else
                {
                    itemFile.Type = ItemFileType.Media;
                }
            }

            return itemFiles;
        }

        private static readonly string[] SupportedImageExtensions = { ".png", ".jpg", ".jpeg", ".webp" };
        private bool IsImageFile(string path)
        {
            var ext = Path.GetExtension(path) ?? string.Empty;

            return SupportedImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        private static readonly string[] SupportedSubtitleExtensions = { ".srt", ".vtt" };
        private bool IsSubtitleFile(string path)
        {
            var ext = Path.GetExtension(path) ?? string.Empty;

            return SupportedSubtitleExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the type of the image.
        /// </summary>
        /// <param name="filename">The filename.</param>
        /// <returns>ImageType.</returns>
        private ImageType GetImageType(string filename)
        {
            return ImageType.Primary;
        }

        /// <summary>
        /// Deletes the specified file.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>Task.</returns>
        public Task DeleteFile(IEnumerable<string> path)
        {
            return _fileRepository.DeleteFile(path);
        }

        public async Task SaveImage(Stream stream,
            string mimeType,
            BaseItemDto item,
            ImageInfo imageInfo,
            ServerInfo server)
        {
            var localFiles = await GetFiles(item, server).ConfigureAwait(false);

            var media = localFiles.FirstOrDefault(i => i.Type == ItemFileType.Media);

            if (media == null)
            {
                throw new ArgumentException("Media not found");
            }

            var imageFilename = GetSaveFileName(media.Name, imageInfo) + GetSaveExtension(mimeType);
            var path = GetDirectoryPath(item, server);
            path.Add(imageFilename);

            await _fileRepository.SaveFile(stream, path);
        }

        private string GetSaveFileName(string mediaName, ImageInfo imageInfo)
        {
            var name = Path.GetFileNameWithoutExtension(mediaName);

            // TODO: Handle other image types

            return name;
        }

        private string GetSaveExtension(string mimeType)
        {
            return MimeTypes.ToExtension(mimeType);
        }

        public Task SaveMedia(Stream stream, SyncedItem jobItem, ServerInfo server)
        {
            var libraryItem = jobItem.Item;

            var filename = jobItem.OriginalFileName;

            if (string.IsNullOrEmpty(filename))
            {
                filename = Guid.NewGuid().ToString("N");
            }

            filename = _fileRepository.GetValidFileName(filename);

            var path = GetDirectoryPath(libraryItem, server);
            path.Add(filename);

            return _fileRepository.SaveFile(stream, path);
        }

        private List<string> GetDirectoryPath(BaseItemDto item, ServerInfo server)
        {
            var parts = new List<string>
            {
                server.Name
            };

            if (item.IsType("movie"))
            {
                parts.Add("Movies");
                parts.Add(item.Name);
            }
            else if (item.IsType("episode"))
            {
                parts.Add("TV");
                parts.Add(item.SeriesName);

                if (!string.IsNullOrWhiteSpace(item.SeasonName))
                {
                    parts.Add(item.SeasonName);
                }
            }
            else if (item.IsVideo)
            {
                parts.Add("Videos");
                parts.Add(item.Name);
            }
            else if (item.IsAudio)
            {
                parts.Add("Music");

                if (!string.IsNullOrWhiteSpace(item.AlbumArtist))
                {
                    parts.Add(item.AlbumArtist);
                }

                if (!string.IsNullOrWhiteSpace(item.Album))
                {
                    parts.Add(item.Album);
                }
            }
            else if (string.Equals(item.MediaType, MediaType.Photo, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add("Photos");

                if (!string.IsNullOrWhiteSpace(item.Album))
                {
                    parts.Add(item.Album);
                }
            }

            return parts.Select(_fileRepository.GetValidFileName).ToList();
        }
    }
}
