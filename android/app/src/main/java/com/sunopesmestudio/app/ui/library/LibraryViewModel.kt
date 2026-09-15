package com.sunopesmestudio.app.ui.library

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import com.sunopesmestudio.app.data.SongDao
import com.sunopesmestudio.app.data.SongEntity
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.flatMapLatest
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

class LibraryViewModel(private val dao: SongDao) : ViewModel() {
    private val query = MutableStateFlow("")

    val searchQuery: StateFlow<String> = query

    @OptIn(kotlinx.coroutines.ExperimentalCoroutinesApi::class)
    val songs: StateFlow<List<SongEntity>> = query
        .flatMapLatest { q -> if (q.isBlank()) dao.observeAll() else dao.search(q) }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())

    val songCount: StateFlow<Int> = dao.observeCount()
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), 0)

    fun setQuery(value: String) {
        query.value = value
    }

    fun toggleFavorite(song: SongEntity) {
        viewModelScope.launch { dao.setFavorite(song.id, !song.isFavorite) }
    }

    fun rate(song: SongEntity, rating: Int) {
        viewModelScope.launch { dao.setRating(song.id, rating) }
    }

    fun delete(song: SongEntity) {
        viewModelScope.launch { dao.delete(song.id) }
    }

    fun importSongs(songs: List<SongEntity>) {
        viewModelScope.launch { dao.upsertAll(songs) }
    }

    class Factory(private val dao: SongDao) : ViewModelProvider.Factory {
        @Suppress("UNCHECKED_CAST")
        override fun <T : ViewModel> create(modelClass: Class<T>): T = LibraryViewModel(dao) as T
    }
}
